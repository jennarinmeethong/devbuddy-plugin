using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DevBuddy.Api.Tests;

/// <summary>
/// The administration UI, served by the API itself.
/// <para>
/// The image <c>docker/Dockerfile.api</c> builds carries <c>web/admin</c>'s output in
/// <c>wwwroot</c>, which is what makes the client and the API one origin and one deployment. A test
/// run has no such build, so these stand a web root up by hand — and the first of them is about
/// what happens when there is none, because that is what running from source does.
/// </para>
/// <para>
/// What is worth proving here is not that a file can be returned. It is that adding a fallback for
/// the client's own routes did not quietly put an HTML page in front of an API that used to answer
/// with JSON, a refusal, or a 404.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AdminUiTests : IDisposable
{
    private const string Marker = "<!-- the administration client -->";

    private readonly ApiFixture _fixture;
    private readonly string _webRoot;
    private readonly WebApplicationFactory<ApiHost> _factory;

    public AdminUiTests(ApiFixture fixture)
    {
        _fixture = fixture;

        _webRoot = Path.Combine(Path.GetTempPath(), "devbuddy-web-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), Marker);
        File.WriteAllText(Path.Combine(_webRoot, "assets", "index-0123456789ab.js"), "export {};");

        _factory = fixture.BuildWithWebRoot(_webRoot);
    }

    public void Dispose()
    {
        _factory.Dispose();

        if (Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task a_host_built_without_the_client_serves_nothing_at_the_root()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task the_root_serves_the_client_to_a_caller_holding_no_token()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(Marker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_route_that_exists_only_in_the_browser_serves_the_client()
    {
        using HttpClient client = _factory.CreateClient();

        // What a person gets by reloading the page they were on, or by following a link into one.
        using HttpResponseMessage response = await client.GetAsync(
            "/w/1e17a3bc-3f61-4a2d-8f4a-5d9f0d9f6d21/audit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Marker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task health_still_answers_as_itself()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task an_authorised_route_still_refuses_instead_of_returning_a_page()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/operations");

        // The failure this guards against is a fallback that answers 200 with HTML, which would
        // leave the client parsing a page as JSON instead of refreshing its token.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task the_method_decides_whether_a_path_belongs_to_the_api_or_the_client()
    {
        using HttpClient client = _factory.CreateClient();

        // Operations are invoked with POST, so a GET of that path matches no endpoint at all and
        // is a navigation as far as this host is concerned: the client is served, and its own
        // router is what decides the address is nowhere. The line that matters is the second one.
        // The route the API does map keeps its own answer — a caller holding no token is refused,
        // rather than handed a page that its JSON parser would choke on.
        using HttpResponseMessage navigation = await client.GetAsync("/operations/no-such-thing");
        using HttpResponseMessage invocation = await client.PostAsync("/operations/no-such-thing", null);

        Assert.Equal(HttpStatusCode.OK, navigation.StatusCode);
        Assert.Contains(
            Marker, await navigation.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Unauthorized, invocation.StatusCode);
    }

    [Fact]
    public async Task a_hashed_asset_is_cached_forever_and_the_page_naming_it_is_not()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage asset = await client.GetAsync("/assets/index-0123456789ab.js");
        using HttpResponseMessage page = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains(
            "immutable",
            asset.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.Ordinal);

        // The one file that must not be cached: it names the hashed assets, so a stale copy pins a
        // browser to a deployment that is gone.
        Assert.True(page.Headers.CacheControl?.NoCache);
    }
}
