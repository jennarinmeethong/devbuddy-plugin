using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DevBuddy.Api.Tests;

/// <summary>
/// The credential endpoints, over HTTP.
/// <para>
/// These are the only routes an unauthenticated caller can reach, which makes them the ones an
/// attacker reaches first. What is proved here is not that sign-in works — that is the easy half —
/// but that failing answers the same way whatever the reason, that a used refresh token cannot be
/// used again, and that spraying is limited.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthenticationEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task signing_in_with_the_right_password_returns_a_token_pair()
    {
        using HttpClient client = fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/sign-in",
            new { email = fixture.AdministratorEmail, password = ApiFixture.AdministratorPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement tokens = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(string.IsNullOrWhiteSpace(tokens.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(tokens.GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task a_wrong_password_and_an_unknown_address_are_answered_identically()
    {
        using HttpClient client = fixture.Factory.CreateClient();

        using HttpResponseMessage wrongPassword = await client.PostAsJsonAsync(
            "/auth/sign-in", new { email = fixture.AdministratorEmail, password = "not-the-password" });

        using HttpResponseMessage unknownAddress = await client.PostAsJsonAsync(
            "/auth/sign-in",
            new { email = $"nobody-{Guid.NewGuid():N}@example.test", password = "not-the-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownAddress.StatusCode);

        // Identical apart from the trace identifier, which differs per request by design. A
        // difference in wording, in status, or in which fields are present would turn this
        // endpoint into a way to find out who has an account here (SB-15).
        Assert.Equal(
            await Described(wrongPassword),
            await Described(unknownAddress));
    }

    [Fact]
    public async Task a_refresh_token_can_be_exchanged_once_and_its_reuse_ends_the_session()
    {
        string email = $"refresh-{Guid.NewGuid():N}@example.test";
        await fixture.CreateUserAsync(email);

        using HttpClient client = fixture.Factory.CreateClient();

        string first = await RefreshTokenFor(client, email);

        using HttpResponseMessage rotated = await client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = first });

        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        JsonElement pair = await rotated.Content.ReadFromJsonAsync<JsonElement>();
        string second = pair.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(first, second);

        // Presenting the retired token is treated as theft rather than as a retry, so the family
        // it belongs to is revoked and the token that replaced it stops working too.
        using HttpResponseMessage replayed = await client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = first });

        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);

        using HttpResponseMessage successor = await client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = second });

        Assert.Equal(HttpStatusCode.Unauthorized, successor.StatusCode);
    }

    [Fact]
    public async Task signing_out_revokes_the_refresh_token()
    {
        string email = $"signout-{Guid.NewGuid():N}@example.test";
        await fixture.CreateUserAsync(email);

        using HttpClient client = fixture.Factory.CreateClient();
        string token = await RefreshTokenFor(client, email);

        using HttpResponseMessage signedOut = await client.PostAsJsonAsync(
            "/auth/sign-out", new { refreshToken = token });

        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);

        using HttpResponseMessage afterwards = await client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = token });

        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Fact]
    public async Task recovery_answers_the_same_way_for_an_address_that_does_not_exist()
    {
        using HttpClient client = fixture.Factory.CreateClient();

        using HttpResponseMessage known = await client.PostAsJsonAsync(
            "/auth/recovery/begin", new { email = fixture.AdministratorEmail });

        using HttpResponseMessage unknown = await client.PostAsJsonAsync(
            "/auth/recovery/begin", new { email = $"nobody-{Guid.NewGuid():N}@example.test" });

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
    }

    [Fact]
    public async Task repeated_wrong_passwords_lock_the_account_out()
    {
        string email = $"lockout-{Guid.NewGuid():N}@example.test";
        await fixture.CreateUserAsync(email);

        using HttpClient client = fixture.Factory.CreateClient();

        // The fixture allows three attempts. The first two are refused as a wrong password; the
        // third exhausts the budget and is answered as locked.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage failed = await client.PostAsJsonAsync(
                "/auth/sign-in", new { email, password = "not-the-password" });

            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        using HttpResponseMessage exhausted = await client.PostAsJsonAsync(
            "/auth/sign-in", new { email, password = "not-the-password" });

        Assert.Equal(HttpStatusCode.Locked, exhausted.StatusCode);

        // And the right password does not lift it. A lockout an attacker can end by guessing
        // correctly once is not a lockout.
        using HttpResponseMessage locked = await client.PostAsJsonAsync(
            "/auth/sign-in", new { email, password = ApiFixture.AdministratorPassword });

        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
    }

    /// <summary>
    /// Control SB-13. Lockout stops an attacker working through one account; this stops them
    /// working through a thousand of them at one a second.
    /// </summary>
    [Fact]
    public async Task the_authentication_endpoints_are_rate_limited()
    {
        await using WebApplicationFactory<ApiHost> constrained = fixture.BuildWith(
            ("RateLimiting:AuthPermitLimit", "3"),
            ("RateLimiting:AuthWindowSeconds", "60"));

        using HttpClient client = constrained.CreateClient();

        List<HttpStatusCode> codes = [];

        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "/auth/sign-in",
                new { email = $"spray-{attempt}@example.test", password = "not-the-password" });

            codes.Add(response.StatusCode);
        }

        Assert.Equal(3, codes.Count(code => code == HttpStatusCode.Unauthorized));
        Assert.Equal(2, codes.Count(code => code == HttpStatusCode.TooManyRequests));
    }

    /// <summary>
    /// The problem details, minus the trace identifier. Everything else has to match.
    /// </summary>
    private static async Task<string> Described(HttpResponseMessage response)
    {
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return string.Join(
            "|",
            body.EnumerateObject()
                .Where(property => !string.Equals(property.Name, "traceId", StringComparison.Ordinal))
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}={property.Value}"));
    }

    private static async Task<string> RefreshTokenFor(HttpClient client, string email)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/sign-in", new { email, password = ApiFixture.AdministratorPassword });

        response.EnsureSuccessStatusCode();

        JsonElement tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        return tokens.GetProperty("refreshToken").GetString()!;
    }
}
