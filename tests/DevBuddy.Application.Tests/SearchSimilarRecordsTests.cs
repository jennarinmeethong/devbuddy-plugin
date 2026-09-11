using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Application.Workers;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Tests;

/// <summary>
/// Semantic search as an operation (ADR-0012), through the real pipeline with fake ports. The
/// index's own behaviour is proved against real pgvector in
/// <c>DevBuddy.Infrastructure.Tests.EmbeddingIndexTests</c>; what is proved here is everything an
/// installation gets wrong before a vector is ever compared.
/// <para>
/// Each of those is an <b>answer with a reason</b> rather than an empty list, because a caller
/// that cannot tell "no similar records" from "this feature is not configured here" will read the
/// second as the first — and will keep asking.
/// </para>
/// </summary>
public sealed class SearchSimilarRecordsTests
{
    [Fact]
    public async Task with_no_provider_the_answer_says_so_and_points_at_full_text_search()
    {
        Harness harness = new();

        SearchSimilarRecordsResponse response = await RunAsync(
            harness, new EmbeddingGateway(harness.Ports), new AvailableIndex());

        Assert.Empty(response.Hits);
        Assert.NotNull(response.Unavailable);
        Assert.Contains("no embedding provider", response.Unavailable, StringComparison.Ordinal);
        Assert.Contains("search_knowledge", response.Unavailable, StringComparison.Ordinal);
    }

    /// <summary>
    /// The condition every shipped stack is in: pgvector is not in <c>postgres:17-alpine</c>, so
    /// the migration skipped itself and there is no table. Said plainly, and without paying a
    /// provider to find out.
    /// </summary>
    [Fact]
    public async Task with_no_vector_index_the_answer_says_so_and_costs_nothing()
    {
        Harness harness = new();
        CountingProvider provider = new();

        SearchSimilarRecordsResponse response = await RunAsync(
            harness, new EmbeddingGateway(harness.Ports, provider), new UnavailableIndex());

        Assert.Contains("pgvector is not installed", response.Unavailable!, StringComparison.Ordinal);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task with_nothing_indexed_the_answer_distinguishes_that_from_no_match()
    {
        Harness harness = new();

        SearchSimilarRecordsResponse response = await RunAsync(
            harness, new EmbeddingGateway(harness.Ports, new CountingProvider()), new AvailableIndex());

        Assert.Empty(response.Hits);
        Assert.Contains("Nothing is indexed", response.Unavailable!, StringComparison.Ordinal);
        Assert.Contains("worker run", response.Unavailable!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A credential pasted into a search box. The pipeline's own SB-17 pass refuses the request
    /// before the use case is entered, because the request declares the query scannable — so the
    /// text never reaches a provider that, on a hosted mode, is outside the trust boundary.
    /// </summary>
    [Fact]
    public async Task a_secret_in_the_query_is_blocked_by_the_pipeline_with_nothing_sent()
    {
        Harness harness = new();
        CountingProvider provider = new();

        SearchSimilarRecordsUseCase useCase = new(
            new EmbeddingGateway(harness.Ports, provider), new AvailableIndex(), harness.Ports);

        // FakePorts treats "SECRET" as a finding, the same corpus every other scannable request is
        // tested against.
        UseCaseResult<SearchSimilarRecordsResponse> result = await harness.RunAsync(
            useCase, new SearchSimilarRecordsRequest(TestData.Scope, "password SECRET please"));

        Assert.Equal(ExecutionOutcome.Blocked, result.Outcome);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public void the_request_offers_the_query_for_scanning()
    {
        // This is what makes the pipeline refusal above possible. The gateway scans again before
        // the call, and the two are not redundant: this protects what is stored and audited, that
        // one protects what leaves.
        SearchSimilarRecordsRequest request = new(TestData.Scope, "a question");

        Assert.Equal(["a question"], request.ContentForScanning);
    }

    [Fact]
    public async Task a_hit_carries_its_title_and_kind_from_the_record_and_not_from_the_index()
    {
        Harness harness = new();
        KnowledgeRecord record = TestData.NewDraft();
        harness.Ports.Record = record;

        SearchSimilarRecordsResponse response = await RunAsync(
            harness,
            new EmbeddingGateway(harness.Ports, new CountingProvider()),
            new IndexReturning([new SimilarRevision(record.Id, 1, 0.05)]));

        Assert.Null(response.Unavailable);
        SimilarRecordHit hit = Assert.Single(response.Hits);
        Assert.Equal(record.Id, hit.RecordId);
        Assert.Equal(record.Kind, hit.Kind);
        Assert.False(string.IsNullOrWhiteSpace(hit.Title));
        Assert.Equal(0.05, hit.Distance);
    }

    /// <summary>
    /// The index is derived and holds no text, so it can be stale. A record deleted since it was
    /// indexed drops out here rather than being reported from a copy the index kept.
    /// </summary>
    [Fact]
    public async Task a_hit_whose_record_is_gone_drops_out_instead_of_being_reported()
    {
        Harness harness = new();
        harness.Ports.Record = null;

        SearchSimilarRecordsResponse response = await RunAsync(
            harness,
            new EmbeddingGateway(harness.Ports, new CountingProvider()),
            new IndexReturning([new SimilarRevision(KnowledgeRecordId.New(), 1, 0.01)]));

        Assert.Empty(response.Hits);
        Assert.Null(response.Unavailable);
    }

    /// <summary>
    /// The scope and the model reach the index as arguments. A store called without a scope would
    /// be free to rank another project's rows, and one called without a model would compare
    /// vectors that are not comparable.
    /// </summary>
    [Fact]
    public async Task the_query_carries_the_requested_scope_and_model_into_the_index()
    {
        Harness harness = new();
        IndexReturning index = new([]);

        await RunAsync(harness, new EmbeddingGateway(harness.Ports, new CountingProvider()), index);

        Assert.Equal(TestData.Scope, index.LastScope);
        Assert.Equal("a fake model", index.LastModel);
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("   ", 10)]
    [InlineData("fine", 0)]
    [InlineData("fine", 51)]
    [InlineData("fine", -1)]
    public void an_invalid_request_is_refused_by_validation(string query, int maxResults)
    {
        Assert.NotEmpty(new SearchSimilarRecordsRequest(TestData.Scope, query, maxResults).Validate());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    public void a_reasonable_request_validates(int maxResults)
    {
        Assert.Empty(new SearchSimilarRecordsRequest(TestData.Scope, "a question", maxResults).Validate());
    }

    [Fact]
    public void the_operation_is_ai_exposed_and_grants_nothing_new()
    {
        // Search is one of the categories info.md permits AI, and this is search. The permission
        // is the one search_knowledge already needs, so exposing this widens the surface by a tool
        // and not by a privilege.
        Assert.Equal(AiExposure.Allowed, UseCaseCatalog.SearchSimilarRecords.AiExposure);
        Assert.Equal(PermissionKind.ReadKnowledge, UseCaseCatalog.SearchSimilarRecords.Permission);
        Assert.True(UseCaseCatalog.SearchSimilarRecords.RedactsOutput);
    }

    private static async Task<SearchSimilarRecordsResponse> RunAsync(
        Harness harness, EmbeddingGateway gateway, IEmbeddingIndex index)
    {
        SearchSimilarRecordsUseCase useCase = new(gateway, index, harness.Ports);

        return await harness.SucceedAsync(
            useCase, new SearchSimilarRecordsRequest(TestData.Scope, "why is the sweep scheduled"));
    }

    private sealed class CountingProvider : IEmbeddingProvider
    {
        public int Calls { get; private set; }

        public string Description => "SelfHosted embeddings, model a fake model";

        public int Dimensions => 3;

        public bool LeavesTheBoundary => false;

        public Task<EmbeddingResult> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(new EmbeddingResult(
                [.. texts.Select(_ => new ReadOnlyMemory<float>([1f, 0f, 0f]))], 1));
        }
    }

    private class AvailableIndex : IEmbeddingIndex
    {
        public virtual Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<int> UpsertAsync(
            ProjectScope scope,
            string model,
            IReadOnlyList<EmbeddedRevision> entries,
            CancellationToken cancellationToken) =>
            Task.FromResult(entries.Count);

        public virtual Task<IReadOnlyList<SimilarRevision>> FindSimilarAsync(
            ProjectScope scope,
            string model,
            ReadOnlyMemory<float> query,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SimilarRevision>>([]);

        public Task<IReadOnlySet<string>> IndexedContentHashesAsync(
            ProjectScope scope, string model, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));

        public Task<int> PurgeProjectAsync(ProjectScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    /// <summary>A database the vector index migration skipped, which is every shipped stack.</summary>
    private sealed class UnavailableIndex : AvailableIndex
    {
        public override Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class IndexReturning(IReadOnlyList<SimilarRevision> hits) : AvailableIndex
    {
        public ProjectScope? LastScope { get; private set; }

        public string? LastModel { get; private set; }

        public override Task<IReadOnlyList<SimilarRevision>> FindSimilarAsync(
            ProjectScope scope,
            string model,
            ReadOnlyMemory<float> query,
            int limit,
            CancellationToken cancellationToken)
        {
            LastScope = scope;
            LastModel = model;
            return Task.FromResult(hits);
        }
    }
}
