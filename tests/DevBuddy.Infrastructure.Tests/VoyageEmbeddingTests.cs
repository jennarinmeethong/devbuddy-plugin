using System.Net;
using System.Text;
using System.Text.Json;
using DevBuddy.Application.Abstractions;
using DevBuddy.Infrastructure.Embeddings;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The hosted provider the owner named, Voyage AI (Phase 13, B6), through the real adapter against
/// a recording double shaped like Voyage's <c>/v1/embeddings</c>. Nothing here reaches the vendor:
/// enabling the hosted mode on an installation needs its own acceptance in <c>info.md</c>.
/// </summary>
public sealed class VoyageEmbeddingTests
{
    private const string Key = "pa-not-a-real-voyage-key";

    [Fact]
    public async Task voyage_is_told_whether_it_is_embedding_a_document_or_a_query()
    {
        var handler = new RecordingHandler();
        HttpEmbeddingProvider provider = Provider(handler, EmbeddingDialect.VoyageAi);

        EmbeddingResult documents = await provider.EmbedAsync(["first", "second"], EmbeddingPurpose.Document, CancellationToken.None);
        await provider.EmbedAsync(["what did we decide?"], EmbeddingPurpose.Query, CancellationToken.None);

        Assert.Equal(2, documents.Vectors.Count);
        Assert.Equal(["document", "query"], handler.Requests.Select(request => request.GetProperty("input_type").GetString()));
        Assert.All(handler.Requests, request => Assert.Equal("voyage-3.5", request.GetProperty("model").GetString()));
        Assert.All(handler.Authorizations, value => Assert.Equal($"Bearer {Key}", value));
        Assert.Equal("https://api.voyageai.com/v1/embeddings", handler.Uris[0]);
    }

    [Fact]
    public async Task an_openai_compatible_provider_is_sent_no_input_type()
    {
        var handler = new RecordingHandler();
        HttpEmbeddingProvider provider = Provider(handler, EmbeddingDialect.OpenAiCompatible);

        await provider.EmbedAsync(["what did we decide?"], EmbeddingPurpose.Query, CancellationToken.None);

        Assert.False(handler.Requests[0].TryGetProperty("input_type", out _));
    }

    [Fact]
    public void the_hosted_mode_starts_only_with_voyages_host_on_the_allow_list()
    {
        EmbeddingOptions options = Options(EmbeddingDialect.VoyageAi);

        Assert.NotEmpty(options.Problems([]));
        Assert.NotEmpty(options.Problems(["api.openai.com"]));
        Assert.Empty(options.Problems(["api.voyageai.com"]));
    }

    [Fact]
    public async Task a_refusal_from_the_vendor_quotes_neither_the_key_nor_the_text()
    {
        var handler = new RecordingHandler { Status = HttpStatusCode.TooManyRequests };
        HttpEmbeddingProvider provider = Provider(handler, EmbeddingDialect.VoyageAi);

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.EmbedAsync(["the importer's private notes"], EmbeddingPurpose.Document, CancellationToken.None));

        Assert.Contains("429", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private notes", refusal.Message, StringComparison.Ordinal);
    }

    private static EmbeddingOptions Options(EmbeddingDialect dialect) => new()
    {
        Provider = EmbeddingProviderKind.HostedApi,
        Dialect = dialect,
        Endpoint = "https://api.voyageai.com/v1",
        Model = "voyage-3.5",
        Dimensions = 3,
        ApiKey = Key,
    };

    private static HttpEmbeddingProvider Provider(RecordingHandler handler, EmbeddingDialect dialect) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options(dialect)));

    private sealed class RecordingHandler : HttpMessageHandler
    {
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

            // Voyage's response: the OpenAI-compatible data array, plus fields this adapter ignores.
            string answer = JsonSerializer.Serialize(new
            {
                @object = "list",
                data = Enumerable.Range(0, count).Select(index => new { @object = "embedding", embedding = new[] { 1f, 0f, 0f }, index }),
                model = "voyage-3.5",
                usage = new { total_tokens = 10 },
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(answer, Encoding.UTF8, "application/json"),
            };
        }
    }
}
