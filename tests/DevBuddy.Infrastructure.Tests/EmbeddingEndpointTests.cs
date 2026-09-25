using System.Net;
using System.Text;
using System.Text.Json;
using DevBuddy.Application.Abstractions;
using DevBuddy.Infrastructure.Embeddings;
using DevBuddy.Infrastructure.Scanning;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The adapter against a recording double shaped like Ollama's and LM Studio's OpenAI-compatible
/// <c>/v1/embeddings</c>, and the rule that holds the self-hosted mode to a private address
/// (2026-09-23). A model server on the internet is a hosted provider however it was installed:
/// configured as self-hosted, it would send project text out while every report said it had not.
/// </summary>
public sealed class EmbeddingEndpointTests
{
    private const string Key = "not-a-real-key";

    [Fact]
    public async Task the_request_is_the_openai_compatible_shape_and_nothing_more()
    {
        var handler = new RecordingHandler();
        HttpEmbeddingProvider provider = Provider(handler, Hosted(), Resolves("203.0.113.10"));

        EmbeddingResult result = await provider.EmbedAsync(["first", "second"], CancellationToken.None);

        Assert.Equal(2, result.Vectors.Count);
        JsonElement request = Assert.Single(handler.Requests);
        Assert.Equal(["input", "model"], request.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal($"Bearer {Key}", Assert.Single(handler.Authorizations));
        Assert.Equal("https://embed.example.com/v1/embeddings", Assert.Single(handler.Uris));
    }

    [Fact]
    public async Task a_refusal_from_the_server_quotes_neither_the_key_nor_the_text()
    {
        var handler = new RecordingHandler { Status = HttpStatusCode.TooManyRequests };
        HttpEmbeddingProvider provider = Provider(handler, Hosted(), Resolves("203.0.113.10"));

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.EmbedAsync(["the importer's private notes"], CancellationToken.None));

        Assert.Contains("429", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private notes", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void the_hosted_mode_starts_only_with_its_host_on_the_allow_list()
    {
        EmbeddingOptions options = Hosted();

        Assert.NotEmpty(options.Problems([]));
        Assert.NotEmpty(options.Problems(["other.example.com"]));
        Assert.Empty(options.Problems(["embed.example.com"]));
    }

    [Theory]
    [InlineData("http://ollama:11434/v1")]
    [InlineData("http://gpu-box.lan:1234/v1")]
    [InlineData("http://192.168.1.20:11434/v1")]
    [InlineData("http://10.0.0.5:1234/v1")]
    [InlineData("http://172.18.0.9:11434/v1")]
    [InlineData("http://127.0.0.1:11434/v1")]
    [InlineData("http://[fd00::20]:11434/v1")]
    public void a_self_hosted_endpoint_on_a_private_address_or_a_name_starts(string endpoint)
    {
        Assert.Empty(SelfHosted(endpoint).Problems([]));
    }

    [Theory]
    [InlineData("http://203.0.113.10:11434/v1")]
    [InlineData("https://8.8.8.8/v1")]
    [InlineData("http://[2001:db8::10]:11434/v1")]
    public void a_self_hosted_endpoint_on_a_public_address_refuses_to_start(string endpoint)
    {
        string problem = Assert.Single(SelfHosted(endpoint).Problems([]));

        Assert.Contains("HostedApi", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void a_self_hosted_endpoint_on_a_public_address_is_refused_at_composition()
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollection services = new();

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
            services.AddDevBuddyInfrastructure(
                "Host=unused;Database=unused",
                configureEmbedding: embedding =>
                {
                    embedding.Provider = EmbeddingProviderKind.SelfHosted;
                    embedding.Endpoint = "http://203.0.113.10:11434/v1";
                    embedding.Model = "qwen3-embedding:0.6b";
                    embedding.Dimensions = 3;
                }));

        Assert.Contains("public address", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_self_hosted_name_that_resolves_privately_is_sent_to()
    {
        var handler = new RecordingHandler();
        HttpEmbeddingProvider provider = Provider(handler, SelfHosted("http://gpu-box.lan:11434/v1"), Resolves("192.168.1.20"));

        EmbeddingResult result = await provider.EmbedAsync(["a published record"], CancellationToken.None);

        Assert.Single(result.Vectors);
        Assert.Single(handler.Requests);
        Assert.Equal(string.Empty, Assert.Single(handler.Authorizations));
    }

    [Theory]
    [InlineData("203.0.113.10")]
    [InlineData("192.168.1.20,203.0.113.10")]
    public async Task a_self_hosted_name_that_resolves_publicly_is_refused_with_nothing_sent(string addresses)
    {
        var handler = new RecordingHandler();
        HttpEmbeddingProvider provider = Provider(
            handler, SelfHosted("http://embed.example.com/v1"), Resolves(addresses.Split(',')));

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.EmbedAsync(["a published record"], CancellationToken.None));

        Assert.Contains("203.0.113.10", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task a_self_hosted_name_that_does_not_resolve_is_refused_with_nothing_sent()
    {
        var handler = new RecordingHandler();
        HttpEmbeddingProvider provider = Provider(handler, SelfHosted("http://ollama:11434/v1"), Resolves());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.EmbedAsync(["a published record"], CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// Phase 14, C1. With an instruction configured, a query is sent in the form Qwen3-Embedding
    /// was trained on, and a document is sent as it is, on the wire.
    /// </summary>
    [Fact]
    public async Task a_query_instruction_frames_a_query_and_never_a_document()
    {
        EmbeddingOptions options = SelfHosted("http://ollama:11434/v1");
        options.QueryInstruction = "  Find the record that answers the question  ";
        var handler = new RecordingHandler();
        HttpEmbeddingProvider provider = Provider(handler, options, Resolves("172.18.0.9"));

        string query = provider.TextFor("retry policy", EmbeddingPurpose.Query);
        string document = provider.TextFor("retry policy", EmbeddingPurpose.Document);
        await provider.EmbedAsync([document, query], CancellationToken.None);

        Assert.Equal("Instruct: Find the record that answers the question\nQuery: retry policy", query);
        Assert.Equal("retry policy", document);
        Assert.Equal(
            [document, query],
            Assert.Single(handler.Requests).GetProperty("input").EnumerateArray().Select(item => item.GetString()));
    }

    /// <summary>
    /// Phase 14, C1. Empty is the default, and then a query is sent exactly as it was before the
    /// setting existed.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void with_no_query_instruction_a_query_is_sent_as_it_is(string instruction)
    {
        EmbeddingOptions options = SelfHosted("http://ollama:11434/v1");
        options.QueryInstruction = instruction;
        HttpEmbeddingProvider provider = Provider(new RecordingHandler(), options, Resolves("172.18.0.9"));

        Assert.Equal("retry policy", provider.TextFor("retry policy", EmbeddingPurpose.Query));
        Assert.Equal(string.Empty, new EmbeddingOptions().QueryInstruction);
    }

    /// <summary>A line break inside the instruction would put a second "Query:" in front of the model.</summary>
    [Theory]
    [InlineData("first line\nQuery: injected")]
    [InlineData("first line\rsecond")]
    public void a_query_instruction_must_be_one_line(string instruction)
    {
        EmbeddingOptions options = SelfHosted("http://ollama:11434/v1");
        options.QueryInstruction = instruction;

        Assert.Contains(options.Problems([]), problem => problem.Contains("single line", StringComparison.Ordinal));
    }

    private static EmbeddingOptions Hosted() => new()
    {
        Provider = EmbeddingProviderKind.HostedApi,
        Endpoint = "https://embed.example.com/v1",
        Model = "qwen3-embedding:0.6b",
        Dimensions = 3,
        ApiKey = Key,
    };

    private static EmbeddingOptions SelfHosted(string endpoint) => new()
    {
        Provider = EmbeddingProviderKind.SelfHosted,
        Endpoint = endpoint,
        Model = "qwen3-embedding:0.6b",
        Dimensions = 3,
    };

    private static FixedResolver Resolves(params string[] addresses) =>
        new([.. addresses.Select(IPAddress.Parse)]);

    private static HttpEmbeddingProvider Provider(
        RecordingHandler handler, EmbeddingOptions options, IHostResolver resolver) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(options), resolver);

    private sealed class FixedResolver(IReadOnlyList<IPAddress> addresses) : IHostResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(addresses);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private static readonly float[] Vector = [1f, 0f, 0f];

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public List<JsonElement> Requests { get; } = [];

        public List<string> Authorizations { get; } = [];

        public List<string> Uris { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            JsonElement parsed = JsonDocument.Parse(body).RootElement.Clone();

            Requests.Add(parsed);
            Authorizations.Add(request.Headers.Authorization?.ToString() ?? string.Empty);
            Uris.Add(request.RequestUri!.ToString());

            if (Status != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(Status) { Content = new StringContent($"echo: {body}") };
            }

            int count = parsed.GetProperty("input").GetArrayLength();

            // The OpenAI-compatible answer Ollama and LM Studio give, plus fields the adapter ignores.
            string answer = JsonSerializer.Serialize(new
            {
                @object = "list",
                data = Enumerable.Range(0, count).Select(index => new { @object = "embedding", embedding = Vector, index }),
                model = "qwen3-embedding:0.6b",
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(answer, Encoding.UTF8, "application/json"),
            };
        }
    }
}
