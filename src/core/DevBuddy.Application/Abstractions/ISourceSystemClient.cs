using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Read-only access to Git and GitHub for authorised repositories.
/// <para>
/// One-way by design (ADR-0010): DevBuddy owns the work item, and nothing is ever written back
/// to the source system. That keeps the required token scope small and removes write-back
/// conflicts entirely.
/// </para>
/// </summary>
public interface ISourceSystemClient
{
    Task<SourceSnapshot> FetchSnapshotAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken);

    Task<ChangeSet> FetchChangeSetAsync(
        SourceRepositoryId repositoryId,
        ProjectScope scope,
        string commitOrRange,
        CancellationToken cancellationToken);

    /// <summary>
    /// Compares a stored snapshot against the origin and reports what moved. Divergence is
    /// surfaced for a human to interpret, never resolved automatically.
    /// </summary>
    Task<IReadOnlyList<SnapshotDifference>> CompareAsync(
        SourceSnapshot earlier, SourceSnapshot later, CancellationToken cancellationToken);
}

/// <summary>
/// What the source system looked like at one moment, with enough metadata to verify a record
/// against its origin later.
/// </summary>
public sealed record SourceSnapshot(
    SourceRepositoryId RepositoryId,
    string Reference,
    string CommitId,
    DateTimeOffset CapturedAt,
    IReadOnlyList<string> Links);

/// <summary>The files a commit or diff touched. Input to change-impact analysis.</summary>
public sealed record ChangeSet(
    string CommitOrRange,
    IReadOnlyList<string> ChangedPaths,
    string Author,
    DateTimeOffset OccurredAt);

/// <summary>One difference between two snapshots.</summary>
public sealed record SnapshotDifference(string Subject, string Before, string After);
