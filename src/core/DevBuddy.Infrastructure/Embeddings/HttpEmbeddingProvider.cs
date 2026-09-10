using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Embeddings;

/// <summary>
/// Both embedding modes, over the one wire format they share.
/// <para>
/// One class rather than two, and that is a considered choice rather than laziness. The self-hosted
/// servers worth pointing at — llama.cpp, Ollama, vLLM, text-embeddings-inference — all speak the
/// OpenAI-compatible <c>/embeddings</c> shape, and so do the hosted APIs. Two classes differing
/// only in a base URL and an <c>Authorization</c> header would be two places to fix the same
/// deserialisation bug.
/// </para>
/// <para>
/// What the two modes genuinely do not share is <b>whether project text leaves the building</b>,
/// and that difference is not expressed here as a branch. It is expressed as
/// <see cref="LeavesTheBoundary"/>, which the gateway reads, and as
/// <see cref="EmbeddingOptions.Problems"/>, which refuses to start a hosted provider whose host
/// nobody put on the outbound allow-list. This class sends what it is given; the decision about
/// whether it may was taken before it was constructed.
/// </para>
/// <para>
/// No retry. A provider that is down is a run that reports a refusal and tries again on its next
/// schedule; a retry loop inside a paid call is a bill nobody authorised, and the budget that
/// bounds the run counts calls rather than attempts.
/// </para>
/// </summary>
internal sealed class HttpEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly EmbeddingOptions _options;

    public HttpEmbeddingProvider(HttpClient http, IOptions<EmbeddingOptions> options)
    {
        _http = Guard.NotNull(http, nameof(http));
        _options = Guard.NotNull(options, nameof(options)).Value;

        _http.BaseAddress = new Uri(
            _options.Endpoint.EndsWith('/') ? _options.Endpoint : _options.Endpoint + "/");

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", _options.ApiKey);
        }
    }

    /// <summary>The mode and the model. Never the key, and never the endpoint's credentials.</summary>
    public string Description => $"{_options.Provider} embeddings, model {_options.Model}";

    public int Dimensions => _options.Dimensions;

    public bool LeavesTheBoundary => _options.Provider == EmbeddingProviderKind.HostedApi;

    public async Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        Guard.NotNull(texts, nameof(texts));

        List<ReadOnlyMemory<float>> vectors = [];
        int calls = 0;

        // Batched because a provider charges and rate-limits per call, and one text per call is
        // the most expensive way to embed anything.
        foreach (string[] batch in Batches(texts, _options.BatchSize))
        {
            HttpResponseMessage response = await _http.PostAsJsonAsync(
                "embeddings", new EmbeddingRequestJson(_options.Model, batch), cancellationToken);

            calls++;

            // The status and nothing else. A provider's error body can quote the request back,
            // and the request is project text.
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"The embedding provider answered {(int)response.StatusCode}. The response "
                    + "body is not included: a provider that echoes its input would put project "
                    + "text into this message.");
            }

            EmbeddingResponseJson? body = await response.Content
                .ReadFromJsonAsync<EmbeddingResponseJson>(cancellationToken);

            if (body?.Data is null)
            {
                throw new InvalidOperationException(
                    "The embedding provider answered with no data array.");
            }

            // Ordered by index rather than by arrival: the OpenAI-compatible shape permits either,
            // and a vector attached to the wrong record is worse than a missing one because
            // nothing about it looks wrong.
            foreach (EmbeddingDatumJson datum in body.Data.OrderBy(item => item.Index))
            {
                vectors.Add(datum.Embedding ?? []);
            }
        }

        return new EmbeddingResult(vectors, calls);
    }

    private static IEnumerable<string[]> Batches(IReadOnlyList<string> texts, int size)
    {
        for (int start = 0; start < texts.Count; start += size)
        {
            yield return [.. texts.Skip(start).Take(size)];
        }
    }

    private sealed record EmbeddingRequestJson(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingResponseJson(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingDatumJson>? Data);

    private sealed record EmbeddingDatumJson(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
