using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Analysis;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Tests;

/// <summary>
/// What the pipeline does when an adapter fails underneath a use case.
/// <para>
/// Until these existed, only a domain rule and a missing resource were translated. A path guard
/// refusing an escape, or a repository with nothing mounted, escaped the executor: the caller got
/// the host's generic error and the audit trail got no row, so the refused escape was invisible to
/// exactly the person who would look for it.
/// </para>
/// </summary>
public sealed class AdapterFailureTests
{
    [Fact]
    public async Task a_guard_refusal_becomes_denied_and_is_audited_as_a_denied_access()
    {
        var harness = new Harness();
        var analyzer = new ThrowingAnalyzer(new GuardRefusalException(
            "path-guard", "The target resolves outside this project's working copy, so nothing was read."));

        UseCaseResult<AnalysisResponse> result = await harness.RunAsync(
            new AnalyzeCodeUseCase(analyzer),
            new AnalysisRequest(TestData.Scope, Target: "../../../etc"),
            TestData.Ai);

        Assert.Equal(ExecutionOutcome.Denied, result.Outcome);
        Assert.Contains("outside", result.Reason, StringComparison.Ordinal);

        // Recorded as an access denial, beside the permission denials, naming the guard.
        AuditEvent entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditAction.AccessDenied, entry.Action);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(TestData.ProjectAlpha, entry.ProjectId);
        Assert.Equal("path-guard", entry.Details["refused_by"]);
        Assert.StartsWith("analyze_code:", entry.ResourceReference, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_missing_working_copy_becomes_not_found_and_is_audited_as_failed()
    {
        var harness = new Harness();
        var sourceSystem = new ThrowingSourceSystem(new ResourceNotFoundException(
            "No working copy is mounted for this repository, so there is nothing to read."));

        UseCaseResult<SnapshotComparisonResponse> result = await harness.RunAsync(
            new CompareSnapshotsUseCase(sourceSystem),
            new CompareSnapshotsRequest(TestData.Scope, TestData.Repository, "main", "release"),
            TestData.Ai);

        Assert.Equal(ExecutionOutcome.NotFound, result.Outcome);
        Assert.Contains("No working copy is mounted", result.Reason, StringComparison.Ordinal);

        AuditEvent entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditAction.RecordViewed, entry.Action);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
    }

    [Fact]
    public async Task an_unavailable_capability_becomes_rejected_with_its_reason_and_is_audited_as_failed()
    {
        var harness = new Harness();
        const string reason = "Analysis:RootPath is not configured for this installation.";
        var sourceSystem = new ThrowingSourceSystem(new OperationUnavailableException(reason));

        UseCaseResult<ChangeImpactResponse> result = await harness.RunAsync(
            new AnalyzeChangeImpactUseCase(sourceSystem, harness.Ports, harness.Ports),
            new AnalyzeChangeImpactRequest(TestData.Scope, TestData.Repository, "main"),
            TestData.Ai);

        Assert.Equal(ExecutionOutcome.Rejected, result.Outcome);
        Assert.Equal(reason, result.Reason);

        AuditEvent entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditAction.AnalysisRun, entry.Action);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal(reason, entry.Details["rejection"]);
    }

    [Fact]
    public async Task an_unexpected_failure_still_propagates_but_is_audited_by_type_and_never_by_message()
    {
        var harness = new Harness();
        var analyzer = new ThrowingAnalyzer(
            new InvalidOperationException("Object is a tree, not a commit, at /srv/private/checkout."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.RunAsync(
            new AnalyzeCodeUseCase(analyzer), new AnalysisRequest(TestData.Scope), TestData.Ai));

        AuditEvent entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditAction.AnalysisRun, entry.Action);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal(nameof(InvalidOperationException), entry.Details["failure"]);

        // The message of a failure nobody anticipated can carry what was read on the way (SB-19).
        Assert.All(entry.Details.Values, value =>
            Assert.DoesNotContain("/srv/private", value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task a_caller_who_cancels_is_not_recorded_as_a_failure()
    {
        var harness = new Harness();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var analyzer = new ThrowingAnalyzer(new OperationCanceledException(cancelled.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Executor.ExecuteAsync(
            new AnalyzeCodeUseCase(analyzer), new AnalysisRequest(TestData.Scope), TestData.Human, cancelled.Token));

        Assert.Empty(harness.Audit.Entries);
    }

    [Fact]
    public async Task when_the_audit_write_also_fails_neither_exception_is_lost()
    {
        var harness = new Harness();
        var executor = new UseCaseExecutor(
            harness.Authorization, new FailingAuditSink(), harness.Ports, harness.Ports, harness.Ports, harness.Ports, harness.Ports);

        var analyzer = new ThrowingAnalyzer(new InvalidOperationException("the original defect"));

        AggregateException both = await Assert.ThrowsAsync<AggregateException>(() => executor.ExecuteAsync(
            new AnalyzeCodeUseCase(analyzer), new AnalysisRequest(TestData.Scope), TestData.Human, CancellationToken.None));

        Assert.Contains(both.InnerExceptions, inner => inner.Message == "the original defect");
        Assert.Contains(both.InnerExceptions, inner => inner.Message == "audit store unavailable");
    }

    private sealed class FailingAuditSink : IAuditSink
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("audit store unavailable"));
    }
}

/// <summary>An analyser that fails the way a real adapter can, for tests about what the pipeline does next.</summary>
internal sealed class ThrowingAnalyzer(Exception failure) : ICodeAnalyzer
{
    public Task<AnalysisReport> AnalyzeAsync(
        AnalysisKind kind, ProjectScope scope, SourceRepositoryId? repositoryId, string? target,
        CancellationToken cancellationToken) =>
        Task.FromException<AnalysisReport>(failure);

    public Task<IReadOnlyList<AnalysisObservation>> AnalyzeChangedPathsAsync(
        ProjectScope scope, SourceRepositoryId repositoryId, IReadOnlyList<string> changedPaths,
        CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<AnalysisObservation>>(failure);
}

/// <summary>A source-system client whose every call fails with one exception.</summary>
internal sealed class ThrowingSourceSystem(Exception failure) : ISourceSystemClient
{
    public Task<SourceSnapshot> FetchSnapshotAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken) =>
        Task.FromException<SourceSnapshot>(failure);

    public Task<ChangeSet> FetchChangeSetAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, string commitOrRange, CancellationToken cancellationToken) =>
        Task.FromException<ChangeSet>(failure);

    public Task<IReadOnlyList<SnapshotDifference>> CompareAsync(
        SourceSnapshot earlier, SourceSnapshot later, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<SnapshotDifference>>(failure);

    public Task<IReadOnlyList<PullRequestSummary>> FetchPullRequestsAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<PullRequestSummary>>(failure);

    public Task<IReadOnlyList<IssueSummary>> FetchIssuesAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<IssueSummary>>(failure);

    public Task<IReadOnlyList<ReviewThreadSummary>> FetchReviewThreadsAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, int pullRequestNumber, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<ReviewThreadSummary>>(failure);
}
