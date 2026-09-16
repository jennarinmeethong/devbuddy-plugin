using System.Diagnostics.CodeAnalysis;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Scanning;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Analysis;

/// <summary>
/// Source snapshots and change sets, read from the mounted working copy.
/// <para>
/// This is the adapter Phase 6 owed. It reads git metadata and objects as files (SB-04): no
/// process, no network, no library that shells out. What it gives up in exchange is anything that
/// only the hosting provider knows — pull requests, issues, review threads — and anything stored
/// in a pack file.
/// </para>
/// <para>
/// ADR-0010 names GitHub as the import source, and that is still the plan. A provider adapter
/// slots in behind the same port; this one exists because a working copy is what a self-hosted
/// deployment already has, and because it can be tested offline, which a live API client cannot.
/// </para>
/// </summary>
internal sealed class WorkingCopySourceSystemClient : ISourceSystemClient
{
    private readonly AnalysisOptions _options;
    private readonly IClock _clock;

    public WorkingCopySourceSystemClient(IOptions<AnalysisOptions> options, IClock clock)
    {
        _options = Guard.NotNull(options, nameof(options)).Value;
        _clock = Guard.NotNull(clock, nameof(clock));
    }

    /// <summary>
    /// What the repository looks like now: the branch HEAD is on, the commit it points at, and
    /// every reference on disk. That is what a record needs in order to be checked against its
    /// origin later (SB-25).
    /// </summary>
    public Task<SourceSnapshot> FetchSnapshotAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken)
    {
        GitObjectStore store = Open(scope, repositoryId);
        GitHead head = store.ReadHead();

        IReadOnlyList<string> links =
        [
            .. store.EnumerateReferences()
                .OrderBy(reference => reference.Key, StringComparer.Ordinal)
                .Select(reference => $"{reference.Key}={reference.Value}")
        ];

        return Task.FromResult(new SourceSnapshot(
            repositoryId, head.Reference, head.CommitId, _clock.UtcNow, links));
    }

    /// <inheritdoc/>
    public Task<ResolvedReference> ResolveReferenceAsync(
        SourceRepositoryId repositoryId,
        ProjectScope scope,
        string reference,
        CancellationToken cancellationToken)
    {
        Guard.NotBlank(reference, nameof(reference));

        return Task.FromResult(ResolveRevision(Open(scope, repositoryId), reference));
    }

    /// <summary>
    /// The paths a commit or a range touched.
    /// <para>
    /// A bare identifier is compared against its first parent; <c>a..b</c> compares the two trees
    /// directly. A merge is compared against its first parent only, which is what makes the answer
    /// "what this merge brought in" rather than "everything on both sides".
    /// </para>
    /// </summary>
    public Task<ChangeSet> FetchChangeSetAsync(
        SourceRepositoryId repositoryId,
        ProjectScope scope,
        string commitOrRange,
        CancellationToken cancellationToken)
    {
        Guard.NotBlank(commitOrRange, nameof(commitOrRange));

        GitObjectStore store = Open(scope, repositoryId);
        (string? fromRevision, string toRevision) = SplitRange(commitOrRange);

        GitCommit target = store.ReadCommit(ResolveRevision(store, toRevision).CommitId);

        string? baseTree = fromRevision is null
            ? target.Parents.Count > 0 ? store.ReadCommit(target.Parents[0]).TreeSha : null
            : store.ReadCommit(ResolveRevision(store, fromRevision).CommitId).TreeSha;

        IReadOnlyDictionary<string, string> after = store.FlattenTree(target.TreeSha, cancellationToken);

        IReadOnlyDictionary<string, string> before = baseTree is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : store.FlattenTree(baseTree, cancellationToken);

        return Task.FromResult(new ChangeSet(
            commitOrRange, ChangedPaths(before, after), target.Author, target.When));
    }

    /// <summary>
    /// What moved between two snapshots.
    /// <para>
    /// Reported, never resolved. A reference that has moved may be ordinary progress or may mean a
    /// record now describes code that no longer exists, and only a person can tell which
    /// (ADR-0010).
    /// </para>
    /// </summary>
    public Task<IReadOnlyList<SnapshotDifference>> CompareAsync(
        SourceSnapshot earlier, SourceSnapshot later, CancellationToken cancellationToken)
    {
        Guard.NotNull(earlier, nameof(earlier));
        Guard.NotNull(later, nameof(later));

        List<SnapshotDifference> differences = [];

        if (!string.Equals(earlier.Reference, later.Reference, StringComparison.Ordinal))
        {
            differences.Add(new SnapshotDifference("HEAD", earlier.Reference, later.Reference));
        }

        if (!string.Equals(earlier.CommitId, later.CommitId, StringComparison.Ordinal))
        {
            differences.Add(new SnapshotDifference(
                earlier.Reference, earlier.CommitId, later.CommitId));
        }

        Dictionary<string, string> before = ToMap(earlier.Links);
        Dictionary<string, string> after = ToMap(later.Links);

        foreach ((string reference, string commit) in before.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!after.TryGetValue(reference, out string? now))
            {
                differences.Add(new SnapshotDifference(reference, commit, "(deleted)"));
            }
            else if (!string.Equals(commit, now, StringComparison.Ordinal))
            {
                differences.Add(new SnapshotDifference(reference, commit, now));
            }
        }

        foreach ((string reference, string commit) in after.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!before.ContainsKey(reference))
            {
                differences.Add(new SnapshotDifference(reference, "(absent)", commit));
            }
        }

        return Task.FromResult<IReadOnlyList<SnapshotDifference>>(differences);
    }

    /// <summary>
    /// A mounted working copy has no record of a pull request, issue, or review — those live only
    /// at the hosting provider. Refused rather than answered as empty: an empty list would read as
    /// "no open pull requests," which is a different, false claim from "this client cannot say."
    /// </summary>
    public Task<IReadOnlyList<PullRequestSummary>> FetchPullRequestsAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "A mounted working copy has no pull request data. Configure GitHub API access to read it.");

    /// <inheritdoc cref="FetchPullRequestsAsync"/>
    public Task<IReadOnlyList<IssueSummary>> FetchIssuesAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "A mounted working copy has no issue data. Configure GitHub API access to read it.");

    /// <inheritdoc cref="FetchPullRequestsAsync"/>
    public Task<IReadOnlyList<ReviewThreadSummary>> FetchReviewThreadsAsync(
        SourceRepositoryId repositoryId,
        ProjectScope scope,
        int pullRequestNumber,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "A mounted working copy has no review data. Configure GitHub API access to read it.");

    /// <summary>Added, removed, and changed paths, in one sorted list.</summary>
    internal static IReadOnlyList<string> ChangedPaths(
        IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        SortedSet<string> changed = new(StringComparer.Ordinal);

        foreach ((string path, string sha) in after)
        {
            if (!before.TryGetValue(path, out string? previous) || previous != sha)
            {
                changed.Add(path);
            }
        }

        foreach (string path in before.Keys)
        {
            if (!after.ContainsKey(path))
            {
                changed.Add(path);
            }
        }

        return [.. changed];
    }

    /// <summary>
    /// Splits <c>a..b</c> into its ends. Anything else is a single revision compared against its
    /// own first parent.
    /// </summary>
    private static (string? From, string To) SplitRange(string commitOrRange)
    {
        int separator = commitOrRange.IndexOf("..", StringComparison.Ordinal);

        return separator < 0
            ? (null, commitOrRange.Trim())
            : (commitOrRange[..separator].Trim(), commitOrRange[(separator + 2)..].Trim());
    }

    /// <summary>
    /// A revision, resolved to the reference it matched and the commit it ends at.
    /// <para>
    /// A full object identifier stands for itself, <c>HEAD</c> for whatever HEAD is on, and a name
    /// beginning <c>refs/</c> for exactly that reference. A bare name is looked up in the order
    /// <c>git rev-parse</c> uses — tags, then branches, then remote-tracking branches — so the
    /// answer is the one a person checking by hand would get, and the name it matched is returned
    /// so that choice is visible rather than silent. Until 2026-09-15 only <c>refs/heads/</c> was
    /// tried, so a bare tag name was not found.
    /// </para>
    /// <para>
    /// Names are looked up among the references the store enumerated, never turned into a path,
    /// so a revision shaped like <c>refs/../HEAD</c> matches nothing rather than reading a file.
    /// </para>
    /// </summary>
    private static ResolvedReference ResolveRevision(GitObjectStore store, string revision)
    {
        string trimmed = revision.Trim();

        if (trimmed.Length == 40 && trimmed.All(Uri.IsHexDigit))
        {
            string id = trimmed.ToLowerInvariant();
            return new ResolvedReference(id, store.PeelToCommit(id));
        }

        if (string.Equals(trimmed, "HEAD", StringComparison.Ordinal))
        {
            GitHead head = store.ReadHead();
            return new ResolvedReference(head.Reference, store.PeelToCommit(head.CommitId));
        }

        IReadOnlyDictionary<string, string> references = store.EnumerateReferences();

        string[] candidates = trimmed.StartsWith("refs/", StringComparison.Ordinal)
            ? [trimmed]
            : [$"refs/tags/{trimmed}", $"refs/heads/{trimmed}", $"refs/remotes/{trimmed}", $"refs/remotes/{trimmed}/HEAD"];

        foreach (string candidate in candidates)
        {
            if (TryFollow(references, candidate, out string? target))
            {
                return new ResolvedReference(candidate, store.PeelToCommit(target));
            }
        }

        throw new ResourceNotFoundException(
            trimmed.StartsWith("refs/", StringComparison.Ordinal)
                ? $"The reference {trimmed} does not exist in this working copy."
                : $"No tag, branch, or remote-tracking branch named {trimmed} exists in this working copy.");
    }

    /// <summary>
    /// The object a reference names, following a symbolic reference (<c>ref: refs/...</c>, the
    /// form <c>refs/remotes/origin/HEAD</c> takes) a bounded number of times.
    /// </summary>
    private static bool TryFollow(
        IReadOnlyDictionary<string, string> references, string name, [NotNullWhen(true)] out string? target)
    {
        string current = name;

        for (int depth = 0; depth < 5; depth++)
        {
            if (!references.TryGetValue(current, out string? value))
            {
                break;
            }

            if (!value.StartsWith("ref:", StringComparison.Ordinal))
            {
                target = value;
                return true;
            }

            current = value["ref:".Length..].Trim();
        }

        target = null;
        return false;
    }

    private static Dictionary<string, string> ToMap(IReadOnlyList<string> links)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string link in links)
        {
            int separator = link.IndexOf('=', StringComparison.Ordinal);

            if (separator > 0)
            {
                map[link[..separator]] = link[(separator + 1)..];
            }
        }

        return map;
    }

    private GitObjectStore Open(ProjectScope scope, SourceRepositoryId repositoryId)
    {
        // Thrown as the pipeline's own types, so each becomes an answer the caller can act on and a
        // row in the audit trail, rather than a generic error from the host and no row at all.
        // An unconfigured root is about the installation; a missing directory is about this
        // repository. The distinction is what tells a caller whether to ask an operator or to
        // check which repository they named.
        if (!_options.IsConfigured)
        {
            throw new OperationUnavailableException(
                "Analysis:RootPath is not configured for this installation, so no repository "
                + "has a working copy to read.");
        }

        if (!_options.IsAnalysable(scope, repositoryId))
        {
            throw new ResourceNotFoundException(
                "No working copy is mounted for this repository, so there is nothing to read. "
                + "Mount it read-only under the analysis root.");
        }

        PathGuard guard = _options.GuardFor(scope, repositoryId).Create();
        var store = new GitObjectStore(guard, _options.MaxFilesVisited);

        return store.Exists
            ? store
            : throw new ResourceNotFoundException("The mounted working copy has no git metadata.");
    }
}
