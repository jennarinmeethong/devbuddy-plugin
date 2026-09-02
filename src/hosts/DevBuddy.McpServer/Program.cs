using System.Security.Claims;
using System.Text;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Hosting;
using DevBuddy.Infrastructure.Identity;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.McpServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Protocol;

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

// The HTTP transport reads the caller from the request principal, so the accessor has to exist.
// Under stdio there is no HTTP context and the caller comes from the environment instead.
builder.Services.AddHttpContextAccessor();

IdentitySettings identitySettings = HostComposition.ReadIdentitySettings(builder.Configuration);

bool useStdio = args.Contains("--stdio", StringComparer.Ordinal);

builder.Services
    .AddMcpServer(options => options.ServerInfo = new Implementation
    {
        Name = "devbuddy",
        Version = typeof(ToolSurface).Assembly.GetName().Version?.ToString() ?? "0.0.0",
    })
    .WithListToolsHandler((request, cancellationToken) =>
        ValueTask.FromResult(Handlers(request.Services!, request.Server).ListTools()))
    .WithCallToolHandler(async (request, cancellationToken) =>
        await Handlers(request.Services!, request.Server)
            .CallToolAsync(request.Params!, cancellationToken));

if (useStdio)
{
    builder.Services.AddMcpServer().WithStdioServerTransport();
}
else
{
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

// Builds the handlers for one call, in the request scope, with the caller resolved from whichever
// transport delivered it. The channel is always Ai here: it is decided by which host is running,
// never by anything in the request (SB-08, SB-09).
static McpToolHandlers Handlers(IServiceProvider services, object? _)
{
    var dispatcher = services.GetRequiredService<OperationDispatcher>();
    var accessor = services.GetService<IHttpContextAccessor>();

    string? subject = accessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Environment.GetEnvironmentVariable("DEVBUDDY_ACTOR");

    UserId actor = Guid.TryParse(subject, out Guid parsed) ? new UserId(parsed) : default;

    return new McpToolHandlers(
        dispatcher,
        new CallerContext(actor, AccessChannel.Ai, Guid.NewGuid().ToString("N")),
        new TenantEntry(
            services.GetRequiredService<MutableTenantContext>(),
            services.GetRequiredService<IWorkspaceResolver>()));
}
