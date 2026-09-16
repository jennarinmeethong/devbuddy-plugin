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
    /// The commit one reference points at now, and the name it was resolved through. An annotated
    /// tag is followed to the commit it tags. Throws <c>ResourceNotFoundException</c> when the
    /// reference does not exist, so a caller is told that rather than handed a fault.
    /// </summary>
    Task<ResolvedReference> ResolveReferenceAsync(
        SourceRepositoryId repositoryId,
        ProjectScope scope,
        string reference,
        CancellationToken cancellationToken);

    /// <summary>
    /// Compares two snapshots of one repository taken at different times and reports what moved.
    /// Divergence is surfaced for a human to interpret, never resolved automatically.
    /// <para>
    /// Two <em>moments</em>, not two references: both arguments must have come from
    /// <see cref="FetchSnapshotAsync"/>. Relabelling one snapshot with two reference names gives two
    /// sides with the same commit, which is how <c>compare_snapshots</c> reported nothing but a
    /// renamed reference until 2026-09-15. Two references are <see cref="ResolveReferenceAsync"/>.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SnapshotDifference>> CompareAsync(
        SourceSnapshot earlier, SourceSnapshot later, CancellationToken cancellationToken);

    /// <summary>
    /// Every pull request, open and closed. Only a client backed by the hosting provider's API
    /// can answer this — a mounted working copy has no record of one that never merged, so that
    /// client refuses rather than guessing.
    /// </summary>
    Task<IReadOnlyList<PullRequestSummary>> FetchPullRequestsAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken);

    /// <summary>Every issue. Same provider-only limitation as <see cref="FetchPullRequestsAsync"/>.</summary>
    Task<IReadOnlyList<IssueSummary>> FetchIssuesAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken);

    /// <summary>Review comments on one pull request. Same provider-only limitation.</summary>
    Task<IReadOnlyList<ReviewThreadSummary>> FetchReviewThreadsAsync(
        SourceRepositoryId repositoryId,
        ProjectScope scope,
        int pullRequestNumber,
        CancellationToken cancellationToken);
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

/// <summary>
/// A reference as the source system resolved it: the name it matched — the full reference name
/// where the client can tell, the identifier itself when one was given — and the commit it points at.
/// </summary>
public sealed record ResolvedReference(string Reference, string CommitId);

/// <summary>The files a commit or diff touched. Input to change-impact analysis.</summary>
public sealed record ChangeSet(
    string CommitOrRange,
    IReadOnlyList<string> ChangedPaths,
    string Author,
    DateTimeOffset OccurredAt);

/// <summary>One difference between two snapshots.</summary>
public sealed record SnapshotDifference(string Subject, string Before, string After);

/// <summary>Untrusted content from the hosting provider — a title and body someone else wrote (SB-01).</summary>
public sealed record PullRequestSummary(
    int Number, string Title, string State, string Author, string Body, DateTimeOffset UpdatedAt, string HtmlUrl);

/// <summary>Untrusted content from the hosting provider (SB-01).</summary>
public sealed record IssueSummary(
    int Number, string Title, string State, string Author, string Body, DateTimeOffset UpdatedAt, string HtmlUrl);

/// <summary>
/// One review comment. <see cref="IsResolved"/> is always false from a REST-backed client: thread
/// resolution is a GraphQL-only field, not exposed by the REST endpoint this reads.
/// </summary>
public sealed record ReviewThreadSummary(
    int PullRequestNumber, string Author, string Body, bool IsResolved, DateTimeOffset CreatedAt);
