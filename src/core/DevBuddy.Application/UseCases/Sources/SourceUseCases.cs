using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.UseCases.Sources;

// Source synchronisation and knowledge quality. All human or internal-system operations: info.md
// keeps source synchronisation and indexing out of the AI surface, so none of these is exposed.

public sealed record SyncSourcesRequest(ProjectScope Scope, SourceRepositoryId RepositoryId)
    : ProjectRequest(Scope)
{
    public override string ResourceReference => RepositoryId.ToString();
}

public sealed record SyncSourcesResponse(
    SourceRepositoryId RepositoryId,
    string CommitId,
    DateTimeOffset CapturedAt,
    int LinkCount,
    // Null rather than 0: null means this source system cannot answer (a mounted working copy),
    // 0 means it answered and found none. A GitHub-backed source answers both.
    int? OpenPullRequestCount,
    int? OpenIssueCount);

/// <summary>
/// Imports a snapshot from an authorised repository.
/// <para>
/// One-way (ADR-0010). Nothing is written back to the source system, which is why this needs
/// only read scope on a GitHub token and why there is no conflict resolution to get wrong.
/// </para>
/// <para>
/// Pull request and issue counts are reported alongside the snapshot when the active source
/// system can answer that question — the GitHub API client can, a mounted working copy cannot,
/// and this reports the difference honestly rather than guessing zero.
/// </para>
/// </summary>
public sealed class SyncSourcesUseCase(ISourceSystemClient sourceSystem)
    : UseCase<SyncSourcesRequest, SyncSourcesResponse>
{
    private readonly ISourceSystemClient _sourceSystem = Guard.NotNull(sourceSystem, nameof(sourceSystem));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.SyncSources;

    protected internal override async Task<SyncSourcesResponse> HandleAsync(
        SyncSourcesRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        SourceSnapshot snapshot =
            await _sourceSystem.FetchSnapshotAsync(request.RepositoryId, request.Scope, cancellationToken);

        int? openPullRequests = null;
        int? openIssues = null;

        try
        {
            IReadOnlyList<PullRequestSummary> pullRequests = await _sourceSystem.FetchPullRequestsAsync(
                request.RepositoryId, request.Scope, cancellationToken);
            openPullRequests = pullRequests.Count(pr => string.Equals(pr.State, "open", StringComparison.OrdinalIgnoreCase));

            IReadOnlyList<IssueSummary> issues = await _sourceSystem.FetchIssuesAsync(
                request.RepositoryId, request.Scope, cancellationToken);
            openIssues = issues.Count(issue => string.Equals(issue.State, "open", StringComparison.OrdinalIgnoreCase));
        }
        catch (NotSupportedException)
        {
            // The active source system has no pull request or issue data — a mounted working
            // copy, today. The snapshot import still succeeded; this is additional detail the
            // response reports as absent rather than as zero.
        }

        return new SyncSourcesResponse(
            snapshot.RepositoryId, snapshot.CommitId, snapshot.CapturedAt, snapshot.Links.Count,
            openPullRequests, openIssues);
    }
}

public sealed record QualitySweepRequest(ProjectScope Scope) : ProjectRequest(Scope)
{
    public override string ResourceReference => "project";
}

public sealed record QualitySweepResponse(string Sweep, IReadOnlyList<QualityFinding> Findings);

/// <summary>
/// Checks that every record can still be traced to its origin. Reports, never repairs: a sweep
/// that silently rewrote provenance would destroy the traceability it is meant to protect.
/// </summary>
public sealed class ValidateProvenanceUseCase(IKnowledgeQualityChecks checks)
    : UseCase<QualitySweepRequest, QualitySweepResponse>
{
    private readonly IKnowledgeQualityChecks _checks = Guard.NotNull(checks, nameof(checks));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ValidateProvenance;

    protected internal override async Task<QualitySweepResponse> HandleAsync(
        QualitySweepRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        IReadOnlyList<QualityFinding> findings =
            await _checks.ValidateProvenanceAsync(request.Scope, cancellationToken);

        return new QualitySweepResponse(Descriptor.Name, findings);
    }
}

/// <summary>Finds records that say the same thing twice.</summary>
public sealed class DetectDuplicatesUseCase(IKnowledgeQualityChecks checks)
    : UseCase<QualitySweepRequest, QualitySweepResponse>
{
    private readonly IKnowledgeQualityChecks _checks = Guard.NotNull(checks, nameof(checks));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.DetectDuplicates;

    protected internal override async Task<QualitySweepResponse> HandleAsync(
        QualitySweepRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        IReadOnlyList<QualityFinding> findings =
            await _checks.DetectDuplicatesAsync(request.Scope, cancellationToken);

        return new QualitySweepResponse(Descriptor.Name, findings);
    }
}

public sealed record DetectStalenessRequest(ProjectScope Scope, TimeSpan StaleAfter) : ProjectRequest(Scope)
{
    public override string ResourceReference => "project";

    public override IReadOnlyList<string> Validate() =>
        StaleAfter <= TimeSpan.Zero ? ["StaleAfter must be a positive duration."] : [];
}

/// <summary>
/// Finds knowledge that has not been touched for long enough to be suspect. Stale records are
/// flagged, never deleted: an old record that is still true is the most valuable kind, and only
/// a human can tell the difference.
/// </summary>
public sealed class DetectStalenessUseCase(IKnowledgeQualityChecks checks, IClock clock)
    : UseCase<DetectStalenessRequest, QualitySweepResponse>
{
    private readonly IKnowledgeQualityChecks _checks = Guard.NotNull(checks, nameof(checks));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.DetectStaleness;

    protected internal override async Task<QualitySweepResponse> HandleAsync(
        DetectStalenessRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        DateTimeOffset staleBefore = _clock.UtcNow - request.StaleAfter;

        IReadOnlyList<QualityFinding> findings =
            await _checks.DetectStalenessAsync(request.Scope, staleBefore, cancellationToken);

        return new QualitySweepResponse(Descriptor.Name, findings);
    }
}

public sealed record ReindexResponse(int DocumentsIndexed);

/// <summary>
/// Rebuilds the search index for one project. The index is derived data: it can always be thrown
/// away and rebuilt from PostgreSQL, which is the source of truth (ADR-0003).
/// </summary>
public sealed class ReindexUseCase(ISearchIndex searchIndex)
    : UseCase<QualitySweepRequest, ReindexResponse>
{
    private readonly ISearchIndex _searchIndex = Guard.NotNull(searchIndex, nameof(searchIndex));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.Reindex;

    protected internal override async Task<ReindexResponse> HandleAsync(
        QualitySweepRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        int indexed = await _searchIndex.ReindexAsync(request.Scope, cancellationToken);
        return new ReindexResponse(indexed);
    }
}
