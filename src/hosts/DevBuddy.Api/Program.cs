using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using DevBuddy.Api;
using DevBuddy.Application.Dispatch;
using DevBuddy.Infrastructure.Hosting;
using DevBuddy.Infrastructure.Identity;
using DevBuddy.Infrastructure.Observability;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// The HTTP surface. Auth endpoints, one route per operation through the dispatcher, an evidence
// stream, and health. Everything except sign-in goes through the same pipeline the MCP server and
// the console use.

// A chiseled runtime image has no curl and no shell, which is the point of one, so the container
// health check runs this executable instead: ask the running server, report by exit code, and
// start nothing.
if (args.Contains("--health-check", StringComparer.Ordinal))
{
    return await HealthProbe.RunAsync(args);
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Environment variables are read with the DEVBUDDY_ prefix as well as bare, so one convention
// covers all three hosts. The console has always read them that way; the servers did not, which
// meant a deployment that configured DEVBUDDY_ConnectionStrings__DevBuddy started the console
// fine and left the API insisting no connection string was configured.
builder.Configuration.AddEnvironmentVariables("DEVBUDDY_");

builder.Services.AddDevBuddy(builder.Configuration, "The API");

// Off unless Logging:File:Path is set, in which case Serilog takes over the pipeline and keeps a
// daily file for the window in Logging:File:RetentionDays. See docs/operations/logging.md for why
// this is a choice rather than a default.
builder.Services.AddDevBuddyFileLogging(builder.Configuration);

// Off unless Telemetry:Endpoint names a collector. See TelemetryOptions for what is exported and
// what is deliberately kept out of it.
//
// The ASP.NET Core pieces are added here rather than in Infrastructure: that package carries a
// framework reference the console must not inherit. The rate limiter's own meter comes with them,
// and its dimensions are the policy and the route template — never the caller (SB-21).
builder.Services.AddDevBuddyTelemetry(
    builder.Configuration,
    "devbuddy-api",
    tracing => tracing.AddAspNetCoreInstrumentation(),
    metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("Microsoft.AspNetCore.RateLimiting"));

IdentitySettings identitySettings = HostComposition.ReadIdentitySettings(builder.Configuration);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = identitySettings.Issuer,
        ValidAudience = identitySettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(identitySettings.SigningKey)),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,

        // Thirty seconds rather than the five-minute default. A short access-token lifetime is
        // what makes an unrevokable signed token acceptable, and a generous skew gives it back.
        ClockSkew = TimeSpan.FromSeconds(30),
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Rate limiting on the authentication endpoints. Lockout already makes guessing one account
// expensive; this makes spraying many of them expensive too (SB-13).
//
// Partitioned by remote address rather than shared, because one shared bucket is itself a denial
// of service: an attacker who exhausts it locks everybody else out of signing in. Configurable
// because the right number depends on whether a reverse proxy is collapsing many people onto one
// address, which only the operator knows.
int authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 10);
int authWindowSeconds = builder.Configuration.GetValue("RateLimiting:AuthWindowSeconds", 60);
int requestPermitLimit = builder.Configuration.GetValue("RateLimiting:RequestPermitLimit", 600);
int requestWindowSeconds = builder.Configuration.GetValue("RateLimiting:RequestWindowSeconds", 60);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromSeconds(authWindowSeconds),
            PermitLimit = authPermitLimit,
            QueueLimit = 0,
        }));

    // And a ceiling on everything else, generous enough that nobody working notices it and low
    // enough that a loop cannot flatten the database (SB-21). Also per caller: signed in, by
    // account, so one person's runaway script does not slow everybody down; signed out, by
    // address, which is all there is to go on.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromSeconds(requestWindowSeconds),
                PermitLimit = requestPermitLimit,
                QueueLimit = 0,
            }));
});

// A ceiling on the body, so an oversized upload is refused at the edge rather than after it has
// been buffered. The evidence store has its own limit for what it will keep; this one is about
// what the process will hold (SB-21).
long maxRequestBytes = builder.Configuration.GetValue("Limits:MaxRequestBytes", 32L * 1024 * 1024);

builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(
    options => options.Limits.MaxRequestBodySize = maxRequestBytes);

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    foreach (System.Text.Json.Serialization.JsonConverter converter in JsonConventions.Options.Converters)
    {
        options.SerializerOptions.Converters.Add(converter);
    }

    options.SerializerOptions.PropertyNamingPolicy = JsonConventions.Options.PropertyNamingPolicy;
});

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous()
    .WithName("Liveness")
    .WithSummary("Liveness only. Component health is an authorised operation, because it names components.");

app.MapAuthentication();
app.MapMe();
app.MapOperations();

await app.RunAsync();
return 0;
