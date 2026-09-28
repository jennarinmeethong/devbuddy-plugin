using System.Security.Claims;
using System.Threading.RateLimiting;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Hosting;
using DevBuddy.Infrastructure.Identity;
using DevBuddy.Infrastructure.Observability;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.McpServer;
using Microsoft.AspNetCore.Authentication;
using ModelContextProtocol.Protocol;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// The MCP server. Two transports, one tool surface.
//
// stdio is what a locally launched Claude or Codex plugin speaks; HTTP is what a self-hosted
// deployment exposes. Both build their tool list from the same catalogue and both run every call
// through the same pipeline, so neither can offer more than the other (SB-07).

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Environment variables are read with the DEVBUDDY_ prefix as well as bare, so one convention
// covers all three hosts. The console has always read them that way; the servers did not, which
// meant a deployment that configured DEVBUDDY_ConnectionStrings__DevBuddy started the console
// fine and left the API insisting no connection string was configured.
builder.Configuration.AddEnvironmentVariables("DEVBUDDY_");

builder.Services.AddDevBuddy(builder.Configuration, "The MCP server");

// Off unless Logging:File:Path is set. Under stdio the console half of it goes to standard error,
// for the same reason the framework logger below does: stdout is the protocol.
builder.Services.AddDevBuddyFileLogging(
    builder.Configuration,
    logToStandardError: args.Contains("--stdio", StringComparer.Ordinal));

// Off unless Telemetry:Endpoint names a collector. The AI channel is the surface where knowing
// what was asked for, and what was refused, matters most.
builder.Services.AddDevBuddyTelemetry(
    builder.Configuration,
    "devbuddy-mcp",
    tracing => tracing.AddAspNetCoreInstrumentation(),
    metrics => metrics.AddAspNetCoreInstrumentation());

// The HTTP transport reads the caller from the request, so the accessor has to exist. Under stdio
// there is no HTTP context and the caller's token comes from the environment instead.
builder.Services.AddHttpContextAccessor();

bool useStdio = args.Contains("--stdio", StringComparer.Ordinal);

builder.Services
    .AddMcpServer(options => options.ServerInfo = new Implementation
    {
        Name = "devbuddy",
        Version = typeof(ToolSurface).Assembly.GetName().Version?.ToString() ?? "0.0.0",
    })
    // Listing tools needs no identity, because the surface does not depend on who is asking: it
    // is the allow-list, the same for everyone, and what any given caller may actually run is
    // decided when they run it.
    .WithListToolsHandler((request, cancellationToken) =>
        ValueTask.FromResult(Surface(request.Services!).ListTools()))
    .WithCallToolHandler(async (request, cancellationToken) =>
        await (await HandlersAsync(request.Services!, cancellationToken))
            .CallToolAsync(request.Params!, cancellationToken));

if (useStdio)
{
    // stdout is the protocol. Anything else written there corrupts the stream, and the default
    // console logger writes there, so logging is moved to stderr before the transport starts.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services.AddMcpServer().WithStdioServerTransport();
}
else
{
    // Registering the transport is not optional and was missing until Phase 10 stood the stack up:
    // MapMcp below throws without it, so every start of this host in HTTP mode crashed. Nothing
    // caught it because nothing had ever run this branch — the stdio path is what the plugins use.
    builder.Services.AddMcpServer().WithHttpTransport();

    // A machine token, the credential stdio takes, and nothing else (Phase 14, A4). The API's
    // access tokens were accepted here until then: fifteen minutes long, which no assistant can
    // refresh, and scoped to no workspace.
    builder.Services
        .AddAuthentication(MachineTokenAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, MachineTokenAuthenticationHandler>(
            MachineTokenAuthenticationHandler.SchemeName, configureOptions: null);

    builder.Services.AddAuthorization();

    // A ceiling per person, the API's own default and the same settings (SB-21): generous enough
    // that nobody working notices it, low enough that an assistant caught in a loop cannot
    // flatten the database. Per person rather than per address, because behind the gateway every
    // caller arrives from one address, and one bucket would let one person's loop stop everybody.
    //
    // A request with no working token is not counted. It is refused before any tool runs, and
    // behind the gateway the only partition it could have is that shared address, so limiting it
    // would hand anybody holding no token at all a way to lock out everybody holding one. A token
    // cannot be guessed (256 random bits); what an unauthenticated flood costs is one indexed
    // lookup each, which ADR-0006's amendment accepts.
    int permitLimit = builder.Configuration.GetValue("RateLimiting:RequestPermitLimit", 600);
    int windowSeconds = builder.Configuration.GetValue("RateLimiting:RequestWindowSeconds", 60);

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } person
                ? RateLimitPartition.GetFixedWindowLimiter(
                    person,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        PermitLimit = permitLimit,
                        QueueLimit = 0,
                    })
                : RateLimitPartition.GetNoLimiter(string.Empty));
    });
}

WebApplication app = builder.Build();

if (!useStdio)
{
    // Authentication before the limiter, so it can partition by person.
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    // At /mcp, so the gateway can pass one path to this service and everything else to the API
    // on the same address and certificate.
    app.MapMcp("/mcp").RequireAuthorization();
}

await app.RunAsync();

// The tool list, which is a question about the catalogue rather than about a caller.
static McpToolHandlers Surface(IServiceProvider services) =>
    new(services.GetRequiredService<OperationDispatcher>(), CallerContext.Anonymous);

// Builds the handlers for one call, in the request scope, with the caller resolved from whichever
// transport delivered it. The channel is always Ai here: it is decided by which host is running,
// never by anything in the request (SB-08, SB-09).
//
// Both transports take a machine token, and since Phase 14 (A4) nothing else. The resolution
// answers with a workspace as well as a person, and the workspace travels into the caller context
// as a ceiling the authorization service enforces: a token minted in one workspace is refused
// every operation naming another, whatever memberships its owner holds there.
//
// Over HTTP the token is the bearer, which MachineTokenAuthenticationHandler has already resolved
// for this request. The environment is never read there: a server process holding a token of its
// own must not lend it to a request that arrived without one.
//
// Over stdio it is DEVBUDDY_TOKEN from the plugin configuration, resolved against the database on
// every call so revoking one takes effect immediately rather than at the next restart.
//
// A token that is unknown, revoked, expired, or left over from before tokens were scoped leaves
// the caller anonymous, and an anonymous caller is refused everything.
static async Task<McpToolHandlers> HandlersAsync(
    IServiceProvider services, CancellationToken cancellationToken)
{
    var dispatcher = services.GetRequiredService<OperationDispatcher>();
    HttpContext? http = services.GetService<IHttpContextAccessor>()?.HttpContext;

    MachineTokenIdentity? identity = null;

    if (http is not null)
    {
        identity = http.Items[typeof(MachineTokenIdentity)] as MachineTokenIdentity;
    }
    else if (Environment.GetEnvironmentVariable("DEVBUDDY_TOKEN") is { Length: > 0 } machineToken)
    {
        identity = await services.GetRequiredService<IMachineTokenService>()
            .ResolveAsync(machineToken, cancellationToken);
    }

    UserId actor = identity?.UserId ?? default;
    CredentialScope? credential = identity is null
        ? null
        : new CredentialScope(identity.TokenId, identity.WorkspaceId);

    return new McpToolHandlers(
        dispatcher,
        new CallerContext(actor, AccessChannel.Ai, Guid.NewGuid().ToString("N"), credential),
        new TenantEntry(
            services.GetRequiredService<MutableTenantContext>(),
            services.GetRequiredService<IWorkspaceResolver>()));
}
