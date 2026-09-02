using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Analysis;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// Source snapshots and change sets, read from a working copy without running git.
/// <para>
/// This is the Phase 6 debt: <c>sync_sources</c>, <c>compare_snapshots</c> and
/// <c>analyze_change_impact</c> threw until now. The fixture writes real git objects, so the
/// reader is tested against the format it claims to read rather than against a mock of it.
/// </para>
/// </summary>
public sealed class SourceSystemTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "devbuddy-git-" + Guid.NewGuid().ToString("N"));

    private readonly ProjectScope _scope = new(WorkspaceId.New(), ProjectId.New());
    private readonly SourceRepositoryId _repository = SourceRepositoryId.New();
    private readonly GitFixture _git;

    public SourceSystemTests()
    {
        Directory.CreateDirectory(WorkingCopy);
        _git = new GitFixture(WorkingCopy);
    }

    private string WorkingCopy =>
        Path.Combine(_root, _scope.ProjectId.Value.ToString(), _repository.Value.ToString());

    private static CancellationToken Ct => CancellationToken.None;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task a_snapshot_reports_the_branch_the_commit_and_every_reference()
    {
        string commit = _git.Commit(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["README.md"] = "# hello",
        });

        _git.SetBranch("main", commit);

        SourceSnapshot snapshot = await Client().FetchSnapshotAsync(_repository, _scope, Ct);

        // Enough to check a record against its origin later (SB-25).
        Assert.Equal("refs/heads/main", snapshot.Reference);
        Assert.Equal(commit, snapshot.CommitId);
        Assert.Contains($"refs/heads/main={commit}", snapshot.Links);
        Assert.Equal(Now, snapshot.CapturedAt);
    }

    [Fact]
    public async Task a_reference_that_has_been_packed_is_still_found()
    {
        string commit = _git.Commit(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["README.md"] = "# hello",
        });

        _git.SetBranch("main", commit);
        _git.SetPackedRefs(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["refs/tags/v1.0"] = commit,
        });

        SourceSnapshot snapshot = await Client().FetchSnapshotAsync(_repository, _scope, Ct);

        // A repository that has been through `git gc` is the normal case, not the exotic one.
        Assert.Contains($"refs/tags/v1.0={commit}", snapshot.Links);
        Assert.Contains($"refs/heads/main={commit}", snapshot.Links);
    }

    [Fact]
    public async Task a_change_set_reports_what_one_commit_added_changed_and_removed()
    {
        string first = _git.Commit(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["README.md"] = "# hello",
            ["src/importer.cs"] = "class Importer { }",
            ["src/legacy.cs"] = "class Legacy { }",
        });

        string second = _git.Commit(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["README.md"] = "# hello",
                ["src/importer.cs"] = "class Importer { void Normalise() { } }",
                ["docs/decision.md"] = "# why",
            },
            parent: first);

        _git.SetBranch("main", second);

        ChangeSet changes = await Client().FetchChangeSetAsync(_repository, _scope, second, Ct);

        Assert.Equal("Jennarin", changes.Author);
        Assert.Contains("src/importer.cs", changes.ChangedPaths);
        Assert.Contains("docs/decision.md", changes.ChangedPaths);
        Assert.Contains("src/legacy.cs", changes.ChangedPaths);

        // Untouched files stay out of it. A change-impact report that listed the whole repository
        // would be worse than none.
        Assert.DoesNotContain("README.md", changes.ChangedPaths);
    }

    [Fact]
    public async Task a_first_commit_reports_everything_it_introduced()
    {
        string first = _git.Commit(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["README.md"] = "# hello",
            ["src/importer.cs"] = "class Importer { }",
        });

        ChangeSet changes = await Client().FetchChangeSetAsync(_repository, _scope, first, Ct);

        Assert.Equal(2, changes.ChangedPaths.Count);
    }

    [Fact]
    public async Task a_range_compares_its_two_ends()
    {
        string first = _git.Commit(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.txt"] = "one",
        });

        string second = _git.Commit(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["a.txt"] = "two" },
            parent: first);

        string third = _git.Commit(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["a.txt"] = "two",
                ["b.txt"] = "new",
            },
            parent: second);

        ChangeSet range = await Client()
            .FetchChangeSetAsync(_repository, _scope, $"{first}..{third}", Ct);

        // Two commits of change, collapsed: a.txt changed and b.txt appeared.
        Assert.Equal(["a.txt", "b.txt"], range.ChangedPaths);
    }

    [Fact]
    public async Task a_branch_name_works_where_an_identifier_does()
    {
        string commit = _git.Commit(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.txt"] = "one",
        });

        _git.SetBranch("main", commit);

        ChangeSet changes = await Client().FetchChangeSetAsync(_repository, _scope, "main", Ct);

        Assert.Single(changes.ChangedPaths);
    }

    [Fact]
    public async Task a_packed_object_is_reported_as_such_rather_than_as_missing_history()
    {
        string first = _git.Commit(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.txt"] = "one",
        });

        string second = _git.Commit(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["a.txt"] = "two" },
            parent: first);

        _git.PackAway(second);

        NotSupportedException failure = await Assert.ThrowsAsync<NotSupportedException>(
            () => Client().FetchChangeSetAsync(_repository, _scope, second, Ct));

        // The failure names the limitation and what to do about it. Returning an empty change set
        // would look like a commit that touched nothing.
        Assert.Contains("not stored loose", failure.Message, StringComparison.Ordinal);
        Assert.Contains("unpack-objects", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task comparing_two_snapshots_reports_what_moved_and_resolves_nothing()
    {
        string first = _git.Commit(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.txt"] = "one",
        });

        _git.SetBranch("main", first);
        WorkingCopySourceSystemClient client = Client();
        SourceSnapshot earlier = await client.FetchSnapshotAsync(_repository, _scope, Ct);

        string second = _git.Commit(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["a.txt"] = "two" },
            parent: first);

        _git.SetBranch("main", second);
        _git.SetBranch("release", first);

        SourceSnapshot later = await client.FetchSnapshotAsync(_repository, _scope, Ct);

        IReadOnlyList<SnapshotDifference> differences = await client.CompareAsync(earlier, later, Ct);

        Assert.Contains(differences, difference =>
            difference.Subject == "refs/heads/main"
            && difference.Before == first
            && difference.After == second);

        Assert.Contains(differences, difference =>
            difference.Subject == "refs/heads/release" && difference.Before == "(absent)");

        // Reported for a person to interpret. Nothing here decides what a moved reference means.
        Assert.All(differences, difference => Assert.NotEqual(difference.Before, difference.After));
    }

    [Fact]
    public async Task a_deleted_branch_is_reported_as_deleted()
    {
        string commit = _git.Commit(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.txt"] = "one",
        });

        _git.SetBranch("main", commit);
        _git.SetBranch("feature", commit);

        WorkingCopySourceSystemClient client = Client();
        SourceSnapshot earlier = await client.FetchSnapshotAsync(_repository, _scope, Ct);

        File.Delete(Path.Combine(WorkingCopy, ".git", "refs", "heads", "feature"));
        SourceSnapshot later = await client.FetchSnapshotAsync(_repository, _scope, Ct);

        Assert.Contains(
            await client.CompareAsync(earlier, later, Ct),
            difference => difference.Subject == "refs/heads/feature" && difference.After == "(deleted)");
    }

    [Fact]
    public async Task a_project_with_no_working_copy_says_so()
    {
        var elsewhere = new ProjectScope(WorkspaceId.New(), ProjectId.New());

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Client().FetchSnapshotAsync(_repository, elsewhere, Ct));

        Assert.Contains("no working copy", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task a_directory_without_git_metadata_says_so()
    {
        var otherRepository = SourceRepositoryId.New();

        Directory.CreateDirectory(Path.Combine(
            _root, _scope.ProjectId.Value.ToString(), otherRepository.Value.ToString()));

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Client().FetchSnapshotAsync(otherRepository, _scope, Ct));

        Assert.Contains("git metadata", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private WorkingCopySourceSystemClient Client() =>
        new(Options.Create(new AnalysisOptions { RootPath = _root }), new FixedClock());

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
