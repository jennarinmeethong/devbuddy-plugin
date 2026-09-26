using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevBuddy.Application;
using DevBuddy.Domain.Work;

namespace DevBuddy.Api.Tests;

/// <summary>
/// What OWASP ZAP's first scans found, fixed (Phase 14, C4).
/// <para>
/// Two requests answered 500: a NUL character in the address given to account recovery, which
/// PostgreSQL cannot store, and a work item key the project already used, whose unique-index
/// violation escaped as an unhandled exception. And no answer carried a security header. Each test
/// here fails against the code before the fix.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ScanFindingTests(ApiFixture fixture)
{
    private const string Nul = "\0";

    // Written out here rather than read from the host, so a header dropped there fails a test.
    private static readonly string[] ExpectedHeaders =
    [
        "Content-Security-Policy",
        "X-Frame-Options",
        "X-Content-Type-Options",
        "Referrer-Policy",
        "Permissions-Policy",
        "Cross-Origin-Resource-Policy",
    ];

    // Honoured only on a secure origin. Over plain HTTP Chromium logs an error for COOP on every
    // page load, so neither is sent unless the request arrived over HTTPS.
    private static readonly string[] SecureOriginOnlyHeaders =
    [
        "Cross-Origin-Opener-Policy",
        "Cross-Origin-Embedder-Policy",
    ];

    [Theory]
    [InlineData("/auth/recovery/begin")]
    [InlineData("/auth/sign-in")]
    public async Task a_nul_character_in_an_address_is_a_bad_request_not_a_server_error(string path)
    {
        using HttpClient client = fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            path, new { email = $"someone{Nul}@example.test", password = "not-the-point-here" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("NUL", problem.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_nul_character_in_an_operations_arguments_is_a_bad_request_not_a_server_error()
    {
        using HttpClient admin = await fixture.SignInAsAdministratorAsync();

        (HttpStatusCode status, JsonElement body) = await Invoke(
            admin,
            UseCaseCatalog.CreateProject.Name,
            new { workspaceId = fixture.Workspace.Value, name = $"Nul{Nul}project" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("NUL", body.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_duplicate_work_item_key_is_refused_with_the_reason_not_a_server_error()
    {
        using HttpClient admin = await fixture.SignInAsAdministratorAsync();

        object scope = new { workspaceId = fixture.Workspace.Value, projectId = fixture.Project.Value };
        string key = $"DUP-{Guid.NewGuid():N}"[..14];

        (HttpStatusCode first, _) = await Invoke(
            admin,
            UseCaseCatalog.CreateWorkItem.Name,
            new { scope, key, type = nameof(WorkItemType.Develop), title = "First", goal = "The one that stays." });

        Assert.Equal(HttpStatusCode.OK, first);

        // Before the fix this was a 500. It was also a 500 even after the violation, because the
        // failed row stayed tracked and the audit entry for the attempt tried to insert it again.
        (HttpStatusCode second, JsonElement body) = await Invoke(
            admin,
            UseCaseCatalog.CreateWorkItem.Name,
            new { scope, key, type = nameof(WorkItemType.FixBug), title = "Second", goal = "Refused." });

        Assert.Equal(HttpStatusCode.Conflict, second);

        string detail = body.GetProperty("detail").GetString()!;
        Assert.Contains(key, detail, StringComparison.Ordinal);
        Assert.Contains("already used", detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/health", HttpStatusCode.OK)]
    [InlineData("/operations", HttpStatusCode.Unauthorized)]
    [InlineData("/no-such-route", HttpStatusCode.NotFound)]
    public async Task every_answer_carries_the_security_headers(string path, HttpStatusCode expected)
    {
        using HttpClient client = fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task a_refusal_written_before_the_handler_runs_carries_them_too()
    {
        using HttpClient client = fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/recovery/begin", new { email = Nul });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task the_secure_origin_policies_are_sent_when_a_proxy_says_the_page_came_over_https()
    {
        using HttpClient client = fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using HttpResponseMessage response = await client.SendAsync(request);

        AssertSecurityHeaders(response, overHttps: true);
        Assert.Equal("same-origin", string.Join(",", response.Headers.GetValues("Cross-Origin-Opener-Policy")));
        Assert.Equal("require-corp", string.Join(",", response.Headers.GetValues("Cross-Origin-Embedder-Policy")));
    }

    [Theory]
    [InlineData("http")]
    [InlineData("http, https")]
    public async Task they_are_not_sent_when_the_browser_reached_the_page_over_plain_http(string forwarded)
    {
        using HttpClient client = fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Forwarded-Proto", forwarded);

        using HttpResponseMessage response = await client.SendAsync(request);

        AssertSecurityHeaders(response);
    }

    internal static void AssertSecurityHeaders(HttpResponseMessage response, bool overHttps = false)
    {
        foreach (string name in ExpectedHeaders)
        {
            Assert.True(
                response.Headers.Contains(name) || response.Content.Headers.Contains(name),
                $"{name} is missing from {response.RequestMessage?.RequestUri}");
        }

        foreach (string name in SecureOriginOnlyHeaders)
        {
            Assert.True(
                response.Headers.Contains(name) == overHttps,
                $"{name} was {(overHttps ? "missing from" : "sent over plain HTTP by")} {response.RequestMessage?.RequestUri}");
        }

        string policy = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("default-src 'self'", policy, StringComparison.Ordinal);
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Invoke(
        HttpClient client, string operation, object arguments)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/operations/{operation}", arguments);

        JsonElement body = response.Content.Headers.ContentLength is > 0
            ? await response.Content.ReadFromJsonAsync<JsonElement>()
            : default;

        return (response.StatusCode, body);
    }
}
