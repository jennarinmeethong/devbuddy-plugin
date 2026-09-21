using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DevBuddy.Api.Tests;

/// <summary>
/// An access token is only as good as the session it was issued for (Phase 13, D6). Until this, a
/// signed token outlived signing out, a revoke-all and a restore for up to its lifetime, and
/// <c>release-matrix.md</c> recorded a pre-disaster token still answering 200 after every drill.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SessionBoundTokenTests(ApiFixture fixture)
{
    [Fact]
    public async Task signing_out_ends_the_access_token_on_its_next_request()
    {
        string email = $"session-out-{Guid.NewGuid():N}@example.test";
        await fixture.CreateUserAsync(email);
        (string access, string refresh) = await SignInAsync(email);

        Assert.Equal(HttpStatusCode.OK, await MeAsync(access));

        using HttpClient anonymous = fixture.Factory.CreateClient();
        using HttpResponseMessage signedOut = await anonymous.PostAsJsonAsync("/auth/sign-out", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(access));
    }

    [Fact]
    public async Task a_session_with_nothing_left_in_the_database_ends_every_token_issued_for_it()
    {
        // What a restore does: sessions are not in a backup, so after it the refresh tokens are gone
        // and the access tokens issued before the disaster must be too.
        string email = $"session-restore-{Guid.NewGuid():N}@example.test";
        UserId user = await fixture.CreateUserAsync(email);
        (string access, _) = await SignInAsync(email);

        Assert.Equal(HttpStatusCode.OK, await MeAsync(access));

        using (IServiceScope scope = fixture.Factory.Services.CreateScope())
        {
            DevBuddyDbContext db = scope.ServiceProvider.GetRequiredService<DevBuddyDbContext>();
            await db.RefreshTokens.Where(token => token.UserId == user.Value).ExecuteDeleteAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(access));
    }

    [Fact]
    public async Task a_rotated_session_keeps_working_and_another_session_is_unaffected()
    {
        string email = $"session-rotate-{Guid.NewGuid():N}@example.test";
        await fixture.CreateUserAsync(email);
        (string first, string firstRefresh) = await SignInAsync(email);
        (string second, _) = await SignInAsync(email);

        using HttpClient anonymous = fixture.Factory.CreateClient();
        using HttpResponseMessage rotated = await anonymous.PostAsJsonAsync("/auth/refresh", new { refreshToken = firstRefresh });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        // The family is still live through its new refresh token, so its earlier access token is
        // too, until it expires; and signing in twice is two sessions, not one.
        Assert.Equal(HttpStatusCode.OK, await MeAsync(first));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(second));
    }

    [Fact]
    public async Task a_correctly_signed_token_that_names_no_session_is_refused()
    {
        string email = $"session-none-{Guid.NewGuid():N}@example.test";
        UserId user = await fixture.CreateUserAsync(email);

        // Shaped like a token issued before this change: valid signature, issuer, audience and
        // lifetime, and no session claim.
        DateTime now = DateTime.UtcNow;
        string forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "devbuddy",
            Audience = "devbuddy",
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFixture.SigningKey)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [ClaimTypes.NameIdentifier] = user.Value.ToString(),
                [JwtRegisteredClaimNames.Sub] = user.Value.ToString(),
            },
        });

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(forged));
    }

    private async Task<(string Access, string Refresh)> SignInAsync(string email)
    {
        using HttpClient client = fixture.Factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/sign-in", new { email, password = ApiFixture.AdministratorPassword });
        response.EnsureSuccessStatusCode();

        JsonElement tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (tokens.GetProperty("accessToken").GetString()!, tokens.GetProperty("refreshToken").GetString()!);
    }

    private async Task<HttpStatusCode> MeAsync(string accessToken)
    {
        using HttpClient client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await client.GetAsync("/me");
        return response.StatusCode;
    }
}
