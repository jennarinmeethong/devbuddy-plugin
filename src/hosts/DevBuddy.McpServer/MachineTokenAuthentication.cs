using System.Security.Claims;
using System.Text.Encodings.Web;
using DevBuddy.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DevBuddy.McpServer;

/// <summary>
/// The HTTP transport's credential: a machine token presented as a bearer (Phase 14, A4).
/// <para>
/// Until A4 this transport took the API's access tokens. Those last fifteen minutes, which no
/// assistant can refresh, and carry no workspace, so a session credential reached every workspace
/// its owner belonged to. A machine token is what the stdio transport already took: minted by its
/// owner under Plugin access, bound to one workspace, and revocable on the next call. Over HTTP it
/// is now the only credential; an access token is refused like any other unknown bearer.
/// </para>
/// <para>
/// Resolved against the database on every request, as stdio resolves it on every call. The MCP
/// transport is stateless, so every tool call is a request of its own, and a revoked token is
/// refused on the next one rather than at the end of a session.
/// </para>
/// </summary>
internal sealed class MachineTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IMachineTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "MachineToken";

    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;

        if (header is null || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        string token = header[BearerPrefix.Length..].Trim();

        if (token.Length == 0)
        {
            return AuthenticateResult.NoResult();
        }

        MachineTokenIdentity? identity = await tokens.ResolveAsync(token, Context.RequestAborted);

        // Unknown, revoked, expired and unscoped all answer the same, as they do over stdio: a
        // caller holding a token that does not work has no business learning which it is.
        if (identity is null)
        {
            return AuthenticateResult.Fail("The bearer is not a machine token that works here.");
        }

        // The principal carries the person, for authorization and the rate limiter. The whole
        // identity travels beside it, so the handlers take the workspace ceiling from what the
        // store answered rather than from a claim parsed back out of a string.
        Context.Items[typeof(MachineTokenIdentity)] = identity;

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, identity.UserId.Value.ToString())],
            SchemeName));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
