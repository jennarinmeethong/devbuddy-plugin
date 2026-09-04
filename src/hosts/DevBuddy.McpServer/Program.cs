using System.Security.Claims;
using System.Text;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Hosting;
using DevBuddy.Infrastructure.Identity;
using DevBuddy.Infrastructure.Observability;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.McpServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
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

// Off unless Telemetry:Endpoint names a collector. The AI channel is the surface where knowing
// what was asked for, and what was refused, matters most.
builder.Services.AddDevBuddyTelemetry(
    builder.Configuration,
    "devbuddy-mcp",
    tracing => tracing.AddAspNetCoreInstrumentation(),
    metrics => metrics.AddAspNetCoreInstrumentation());

// The HTTP transport reads the caller from the request principal, so the accessor has to exist.
// Under stdio there is no HTTP context and the caller presents a machine token instead.
builder.Services.AddHttpContextAccessor();

IdentitySettings identitySettings = HostComposition.ReadIdentitySettings(builder.Configuration);

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

    // The HTTP transport shares the API tokens and therefore the API identity. There is no
    // separate MCP credential to get wrong.
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = identitySettings.Issuer,
            ValidAudience = identitySettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(identitySettings.SigningKey)),
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        });

    builder.Services.AddAuthorization();
}

WebApplication app = builder.Build();

if (!useStdio)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapMcp().RequireAuthorization();
}

await app.RunAsync();

// The tool list, which is a question about the catalogue rather than about a caller.
static McpToolHandlers Surface(IServiceProvider services) =>
    new(services.GetRequiredService<OperationDispatcher>(), CallerContext.Anonymous);

// Builds the handlers for one call, in the request scope, with the caller resolved from whichever
// transport delivered it. The channel is always Ai here: it is decided by which host is running,
// never by anything in the request (SB-08, SB-09).
//
// Over HTTP that is the bearer token the API issues. Over stdio it is a machine token from the
// plugin configuration, resolved against the database on every call so revoking one takes effect
// immediately rather than at the next restart. A token that is unknown, revoked, or expired
// leaves the caller anonymous, and an anonymous caller is refused everything.
static async Task<McpToolHandlers> HandlersAsync(
    IServiceProvider services, CancellationToken cancellationToken)
{
    var dispatcher = services.GetRequiredService<OperationDispatcher>();
    var accessor = services.GetService<IHttpContextAccessor>();

    UserId actor = default;

    string? subject = accessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    if (Guid.TryParse(subject, out Guid parsed))
    {
        actor = new UserId(parsed);
    }
    else if (Environment.GetEnvironmentVariable("DEVBUDDY_TOKEN") is { Length: > 0 } machineToken)
    {
        actor = await services.GetRequiredService<IMachineTokenService>()
            .ResolveAsync(machineToken, cancellationToken) ?? default;
    }

    return new McpToolHandlers(
        dispatcher,
        new CallerContext(actor, AccessChannel.Ai, Guid.NewGuid().ToString("N")),
        new TenantEntry(
            services.GetRequiredService<MutableTenantContext>(),
            services.GetRequiredService<IWorkspaceResolver>()));
}
