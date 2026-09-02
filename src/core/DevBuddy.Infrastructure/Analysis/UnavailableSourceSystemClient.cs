using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Infrastructure.Analysis;

/// <summary>
/// Source synchronisation is **not implemented**. This type exists so the container resolves and
/// every call says so loudly.
/// <para>
/// Phase 6 delivered analysis and the safety gates. GitHub import did not fit alongside them and,
/// more to the point, could not have been tested here: a client written against a live API with no
/// coverage is exactly the kind of surface this project keeps refusing to claim. The port is
/// unchanged, so adding the adapter later is a registration change and nothing else.
/// </para>
/// <para>
/// Three use cases depend on it and will fail until then: sync_sources, compare_snapshots, and
/// analyze_change_impact. The verification matrix and docs/plan.md say so.
/// </para>
/// </summary>
internal sealed class UnavailableSourceSystemClient : ISourceSystemClient
{
    private const string Explanation =
        "Source synchronisation is not implemented. sync_sources, compare_snapshots, and "
        + "analyze_change_impact need a source-system client, and none ships yet.";

    public Task<SourceSnapshot> FetchSnapshotAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException(Explanation);

    public Task<ChangeSet> FetchChangeSetAsync(
        SourceRepositoryId repositoryId,
        ProjectScope scope,
        string commitOrRange,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(Explanation);

    public Task<IReadOnlyList<SnapshotDifference>> CompareAsync(
        SourceSnapshot earlier, SourceSnapshot later, CancellationToken cancellationToken) =>
        throw new NotSupportedException(Explanation);
}
