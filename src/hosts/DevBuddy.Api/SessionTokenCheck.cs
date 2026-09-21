using System.Security.Claims;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace DevBuddy.Api;

/// <summary>
/// Refuses an access token whose session has ended (Phase 13, D6).
/// <para>
/// A signed access token is valid by itself until it expires. Before this, signing out, a
/// revoke-all after a password recovery, and a restore (which brings no session back) all left
/// every access token already issued working for up to its lifetime. Each token now names the
/// refresh-token family it was issued for, and a token whose family has nothing live left is
/// refused on its next request. A token issued before this carries no session and is refused too,
/// so everybody signs in once after upgrading.
/// </para>
/// <para>
/// In the host rather than in Infrastructure because it is ASP.NET Core's event model, and that
/// framework reference must not reach the console image. The same class exists in the MCP server,
/// whose HTTP transport takes the same tokens.
/// </para>
/// </summary>
internal static class SessionTokenCheck
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        string? subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        string? session = context.Principal?.FindFirstValue(SessionTokenClaims.Session);

        if (!Guid.TryParse(subject, out Guid user) || !Guid.TryParse(session, out Guid family))
        {
            context.Fail("The access token names no session. Sign in again.");
            return;
        }

        ITokenService tokens = context.HttpContext.RequestServices.GetRequiredService<ITokenService>();

        if (!await tokens.IsSessionLiveAsync(new UserId(user), family, context.HttpContext.RequestAborted))
        {
            context.Fail("The session this access token belongs to has ended. Sign in again.");
        }
    }
}
