using System.Text;
using System.Threading.RateLimiting;
using DevBuddy.Api;
using DevBuddy.Application.Dispatch;
using DevBuddy.Infrastructure.Hosting;
using DevBuddy.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

// The HTTP surface. Auth endpoints, one route per operation through the dispatcher, an evidence
// stream, and health. Everything except sign-in goes through the same pipeline the MCP server and
// the console use.

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddDevBuddy(builder.Configuration, "The API");

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
});

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
app.MapOperations();

await app.RunAsync();
