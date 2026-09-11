using System.Text.Json;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.Workers;
using DevBuddy.Application.Workers.Jobs;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The job that fills the vector index, and the only thing in this system that writes one.
/// <para>
/// The dispatcher is real and only its bindings are fakes, so the AI-channel refusal is exercised
/// rather than mocked away. That matters more here than anywhere: this job runs on the AI channel
/// because the per-project access policy and the SB-18 redaction hang off it, and a test that
/// faked the dispatcher would prove nothing about either.
/// </para>
/// </summary>
public sealed class RecordEmbeddingSweepJobTests
{
    private const string Model = "a fake model";

    [Fact]
    public void the_job_declares_that_it_sends_content_to_a_model()
    {
        // Which is what puts it on the AI channel, which is what makes it safe. See
        // WorkerModelUse: on InternalSystem it would escape the per-project AI access policy and
        // could embed a project nobody opened to AI.
        RecordEmbeddingSweepJob job = Job(new RecordingDispatcher(), Configured(), new FakeIndex());

        Assert.Equal(WorkerModelUse.SendsContentToAModel, job.ModelUse);
    }

    [Fact]
    public async Task with_no_provider_the_run_is_refused_before_anything_is_listed()
    {
        RecordingDispatcher recorder = new();

        WorkerRunReport report = await Run(
            Job(recorder, new EmbeddingGateway(new CleanScanner()), new FakeIndex()));

        Assert.True(report.Refused);
        Assert.Contains("No embedding provider", report.Reason!, StringComparison.Ordinal);
        Assert.Empty(recorder.Invoked);
    }

    [Fact]
    public async Task with_no_vector_index_the_run_is_refused_before_anything_is_listed()
    {
        RecordingDispatcher recorder = new();
        FakeIndex index = new() { Available = false };

        WorkerRunReport report = await Run(Job(recorder, Configured(), index));

        Assert.True(report.Refused);
        Assert.Contains("pgvector is not installed", report.Reason!, StringComparison.Ordinal);
        Assert.Empty(recorder.Invoked);
    }

    [Fact]
    public async Task a_published_revision_is_embedded_and_written_with_its_hash()
    {
        Guid project = Guid.NewGuid();
        Guid record = Guid.NewGuid();

        RecordingDispatcher recorder = new();
        recorder.Projects(project);
        recorder.Records(record);
        recorder.History(publishedRevision: 3, contentHash: "HASH-3");
        recorder.Record("Why the sweep is scheduled", "Because a window nothing sweeps is a window in name only.");

        FakeIndex index = new();
        CountingProvider provider = new();

        WorkerRunReport report = await Run(Job(recorder, Configured(provider), index));

        Assert.False(report.Refused);
        Assert.Equal(1, report.CallsSpent);

        EmbeddedRevision written = Assert.Single(index.Written);
        Assert.Equal(record, written.RecordId.Value);
        Assert.Equal(3, written.RevisionNumber);
        Assert.Equal("HASH-3", written.ContentHash);

        Assert.Equal(Model, index.LastModel);
        Assert.Equal(project, index.LastScope!.Value.ProjectId.Value);

        // Title and body, joined. A record whose title carries the subject and whose body carries
        // the reasoning is badly served by embedding either alone.
        string embedded = Assert.Single(provider.Texts);
        Assert.Contains("Why the sweep is scheduled", embedded, StringComparison.Ordinal);
        Assert.Contains("window in name only", embedded, StringComparison.Ordinal);
    }

    /// <summary>
    /// The difference between a nightly sweep that costs nothing on a quiet installation and one
    /// that re-bills the whole corpus every night.
    /// </summary>
    [Fact]
    public async Task a_revision_already_indexed_costs_nothing()
    {
        RecordingDispatcher recorder = new();
        recorder.Projects(Guid.NewGuid());
        recorder.Records(Guid.NewGuid());
        recorder.History(publishedRevision: 2, contentHash: "HASH-2");
        recorder.Record("Title", "Body");

        FakeIndex index = new() { Indexed = new HashSet<string>(["HASH-2"], StringComparer.Ordinal) };
        CountingProvider provider = new();

        RecordEmbeddingSweepJob job = Job(recorder, Configured(provider), index);
        WorkerRunReport report = await Run(job);

        Assert.Equal(0, report.CallsSpent);
        Assert.Empty(provider.Texts);
        Assert.Empty(index.Written);

        EmbeddingSweepSummary summary = Assert.Single(job.Summaries);
        Assert.Equal(1, summary.AlreadyCurrent);
        Assert.Equal(0, summary.Embedded);

        // And get_record was never called, so an unchanged record costs neither a read nor an
        // embedding.
        Assert.DoesNotContain("get_record", recorder.Invoked);
    }

    /// <summary>
    /// A draft is not knowledge yet, and indexing one would let a semantic search surface
    /// something nobody approved — the same mistake as publishing on a stale approval, reached
    /// from a different direction.
    /// </summary>
    [Fact]
    public async Task a_record_with_no_published_revision_is_not_embedded()
    {
        RecordingDispatcher recorder = new();
        recorder.Projects(Guid.NewGuid());
        recorder.Records(Guid.NewGuid());
        recorder.HistoryWithNoPublishedRevision();

        FakeIndex index = new();
        CountingProvider provider = new();

        RecordEmbeddingSweepJob job = Job(recorder, Configured(provider), index);
        await Run(job);

        Assert.Empty(provider.Texts);
        Assert.Empty(index.Written);
        Assert.Equal(1, Assert.Single(job.Summaries).Skipped);
    }

    /// <summary>
    /// SB-17 at the gateway, from inside a background job: the record is skipped with nothing
    /// sent and nothing written, and the sweep carries on to the next one.
    /// </summary>
    [Fact]
    public async Task a_record_carrying_a_secret_is_skipped_with_nothing_written()
    {
        RecordingDispatcher recorder = new();
        recorder.Projects(Guid.NewGuid());
        recorder.Records(Guid.NewGuid());
        recorder.History(publishedRevision: 1, contentHash: "HASH-1");
        recorder.Record("Title", "Password=hunter2");

        FakeIndex index = new();
        CountingProvider provider = new();

        RecordEmbeddingSweepJob job = Job(
            recorder,
            new EmbeddingGateway(new ScannerThatFinds("assigned-secret"), provider),
            index);

        WorkerRunReport report = await Run(job);

        Assert.False(report.Refused);
        Assert.Empty(provider.Texts);
        Assert.Empty(index.Written);
        Assert.Equal(1, Assert.Single(job.Summaries).Skipped);
    }

    [Fact]
    public async Task a_budget_that_runs_out_stops_spending_and_says_what_it_missed()
    {
        Guid[] records = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        RecordingDispatcher recorder = new();
        recorder.Projects(Guid.NewGuid());
        recorder.Records(records);
        recorder.History(publishedRevision: 1, contentHash: "HASH-1");
        recorder.Record("Title", "Body");

        FakeIndex index = new();
        CountingProvider provider = new();

        RecordEmbeddingSweepJob job = Job(recorder, Configured(provider), index);

        // Two texts of budget for three records.
        WorkerRunReport report = await Run(job, new WorkerBudget(2));

        Assert.False(report.Refused);
        Assert.Equal(2, report.CallsSpent);
        Assert.Equal(2, provider.Texts.Count);

        EmbeddingSweepSummary summary = Assert.Single(job.Summaries);
        Assert.Equal(2, summary.Embedded);
        Assert.Equal(1, summary.Skipped);
    }

    /// <summary>
    /// A project the credential lists but cannot read, or one whose AI policy closed between the
    /// two calls. Reported per project rather than thrown, because it tells an operator the token
    /// is narrower than the sweep they asked for.
    /// </summary>
    [Fact]
    public async Task a_project_whose_records_cannot_be_listed_is_reported_and_the_run_continues()
    {
        RecordingDispatcher recorder = new();
        recorder.Projects(Guid.NewGuid());
        recorder.Refuse("list_records", "The caller has no access to this scope.");

        RecordEmbeddingSweepJob job = Job(recorder, Configured(), new FakeIndex());
        WorkerRunReport report = await Run(job);

        Assert.False(report.Refused);
        EmbeddingSweepSummary summary = Assert.Single(job.Summaries);
        Assert.Contains("no access", summary.Refusal!, StringComparison.Ordinal);
        Assert.Equal(0, summary.Embedded);
    }

    /// <summary>
    /// The job never asks about a project the AI channel cannot see, because
    /// <c>list_projects</c> on that channel omits one whose owner never enabled access. This
    /// asserts the job adds no second route: it sweeps exactly what it was told about.
    /// </summary>
    [Fact]
    public async Task the_sweep_only_touches_the_projects_it_was_listed()
    {
        RecordingDispatcher recorder = new();
        recorder.Projects();

        RecordEmbeddingSweepJob job = Job(recorder, Configured(), new FakeIndex());
        WorkerRunReport report = await Run(job);

        Assert.False(report.Refused);
        Assert.Empty(job.Summaries);
        Assert.Equal(["list_projects"], recorder.Invoked);
    }

    private static RecordEmbeddingSweepJob Job(
        RecordingDispatcher recorder, EmbeddingGateway gateway, FakeIndex index) =>
        new(recorder.Build(), gateway, index);

    private static EmbeddingGateway Configured(CountingProvider? provider = null) =>
        new(new CleanScanner(), provider ?? new CountingProvider());

    private static async Task<WorkerRunReport> Run(
        RecordEmbeddingSweepJob job, WorkerBudget? budget = null)
    {
        WorkerCaller caller = WorkerCaller.FromMachineToken(
            new MachineTokenIdentity(
                new MachineTokenId(Guid.NewGuid()),
                new UserId(Guid.NewGuid()),
                new WorkspaceId(Guid.NewGuid()),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(7)),
            "run-1",
            job.ModelUse)!;

        return await job.RunAsync(caller, budget ?? new WorkerBudget(100), CancellationToken.None);
    }

    private sealed class CleanScanner : ISecretScanner
    {
        public Task<SecretScanResult> ScanAsync(string content, CancellationToken cancellationToken) =>
            Task.FromResult(SecretScanResult.Clean);
    }

    private sealed class ScannerThatFinds(string rule) : ISecretScanner
    {
        public Task<SecretScanResult> ScanAsync(string content, CancellationToken cancellationToken) =>
            Task.FromResult(new SecretScanResult([new SecretFinding(rule, 1, 8)]));
    }

    private sealed class CountingProvider : IEmbeddingProvider
    {
        public List<string> Texts { get; } = [];

        public string Description => $"SelfHosted embeddings, model {Model}";

        public int Dimensions => 3;

        public bool LeavesTheBoundary => false;

        public Task<EmbeddingResult> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            Texts.AddRange(texts);

            return Task.FromResult(new EmbeddingResult(
                [.. texts.Select(_ => new ReadOnlyMemory<float>([1f, 0f, 0f]))], 1));
        }
    }

    private sealed class FakeIndex : IEmbeddingIndex
    {
        public bool Available { get; init; } = true;

        public IReadOnlySet<string> Indexed { get; init; } = new HashSet<string>(StringComparer.Ordinal);

        public List<EmbeddedRevision> Written { get; } = [];

        public ProjectScope? LastScope { get; private set; }

        public string? LastModel { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Available);

        public Task<int> UpsertAsync(
            ProjectScope scope,
            string model,
            IReadOnlyList<EmbeddedRevision> entries,
            CancellationToken cancellationToken)
        {
            LastScope = scope;
            LastModel = model;
            Written.AddRange(entries);
            return Task.FromResult(entries.Count);
        }

        public Task<IReadOnlyList<SimilarRevision>> FindSimilarAsync(
            ProjectScope scope,
            string model,
            ReadOnlyMemory<float> query,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SimilarRevision>>([]);

        public Task<IReadOnlySet<string>> IndexedContentHashesAsync(
            ProjectScope scope, string model, CancellationToken cancellationToken) =>
            Task.FromResult(Indexed);

        public Task<int> PurgeProjectAsync(ProjectScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    /// <summary>
    /// A real dispatcher over fake bindings, bound under the operations' real catalogue
    /// descriptors so the AI exposure the product declares is the exposure under test.
    /// </summary>
    private sealed class RecordingDispatcher
    {
        private readonly Dictionary<string, Func<DispatchResult>> _answers = new(StringComparer.Ordinal);

        public List<string> Invoked { get; } = [];

        public void Projects(params Guid[] ids) =>
            Answer("list_projects", new { projects = ids.Select(id => new { projectId = id }) });

        public void Records(params Guid[] ids) =>
            Answer("list_records", new { records = ids.Select(id => new { recordId = id }) });

        public void History(int publishedRevision, string contentHash) =>
            Answer("view_record_history", new
            {
                revisions = new[]
                {
                    new { revisionNumber = publishedRevision - 1, contentHash = "older", isPublished = false },
                    new { revisionNumber = publishedRevision, contentHash, isPublished = true },
                },
            });

        public void HistoryWithNoPublishedRevision() =>
            Answer("view_record_history", new
            {
                revisions = new[] { new { revisionNumber = 1, contentHash = "draft", isPublished = false } },
            });

        public void Record(string title, string body) =>
            Answer("get_record", new { title, body });

        public void Refuse(string operation, string reason) =>
            _answers[operation] = () => new DispatchResult(
                operation, ExecutionOutcome.Denied, null, reason, []);

        public OperationDispatcher Build()
        {
            List<OperationBinding> bindings = [];

            foreach ((string operation, Func<DispatchResult> answer) in _answers)
            {
                bindings.Add(new OperationBinding(
                    UseCaseCatalog.All.First(candidate => candidate.Name == operation),
                    typeof(object),
                    typeof(object),
                    (_, _, _) =>
                    {
                        Invoked.Add(operation);
                        return Task.FromResult(answer());
                    }));
            }

            return new OperationDispatcher(bindings);
        }

        private void Answer(string operation, object payload) =>
            _answers[operation] = () => new DispatchResult(
                operation,
                ExecutionOutcome.Succeeded,
                JsonSerializer.SerializeToElement(payload, JsonConventions.Options),
                string.Empty,
                []);
    }
}
