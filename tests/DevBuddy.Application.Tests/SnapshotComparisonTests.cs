using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Reading;

namespace DevBuddy.Application.Tests;

/// <summary>
/// <c>compare_snapshots</c> resolves each side on its own. Until 2026-09-15 it fetched one snapshot
/// and relabelled it twice, so both sides carried the same commit and a move was never reported —
/// and the old test here passed, because the fake answered in the same shape the bug produced.
/// </summary>
public sealed class SnapshotComparisonTests
{
    private const string First = "1111111111111111111111111111111111111111";
    private const string Second = "2222222222222222222222222222222222222222";

    [Fact]
    public async Task the_commit_move_between_two_references_is_reported()
    {
        var harness = new Harness();

        SnapshotComparisonResponse response = await harness.SucceedAsync(
            new CompareSnapshotsUseCase(harness.Ports),
            new CompareSnapshotsRequest(TestData.Scope, TestData.Repository, "v1", "v2"));

        Assert.Equal(First, response.Earlier.CommitId);
        Assert.Equal(Second, response.Later.CommitId);

        SnapshotDifference moved = Assert.Single(response.Differences);
        Assert.Equal("commit", moved.Subject);
        Assert.Equal(First, moved.Before);
        Assert.Equal(Second, moved.After);

        Assert.Equal(["src/importer.cs"], response.ChangedPaths);
        Assert.Null(response.ChangedPathsUnavailable);

        // The paths are read between the commits that were reported, not between the names, which
        // could have moved in between.
        Assert.Equal($"{First}..{Second}", harness.Ports.LastCommitOrRange);
    }

    [Fact]
    public async Task two_references_at_the_same_commit_report_no_difference()
    {
        var harness = new Harness();
        harness.Ports.References["main"] = Second;

        SnapshotComparisonResponse response = await harness.SucceedAsync(
            new CompareSnapshotsUseCase(harness.Ports),
            new CompareSnapshotsRequest(TestData.Scope, TestData.Repository, "v2", "main"));

        Assert.Empty(response.Differences);
        Assert.Empty(response.ChangedPaths!);
        Assert.Null(harness.Ports.LastCommitOrRange);
    }

    [Fact]
    public async Task the_move_is_still_reported_when_the_paths_cannot_be_read()
    {
        var harness = new Harness();
        harness.Ports.ChangeSetsUnsupported = true;

        SnapshotComparisonResponse response = await harness.SucceedAsync(
            new CompareSnapshotsUseCase(harness.Ports),
            new CompareSnapshotsRequest(TestData.Scope, TestData.Repository, "v1", "v2"));

        Assert.Single(response.Differences);

        // Absent with a reason, not empty: an empty list would say nothing changed.
        Assert.Null(response.ChangedPaths);
        Assert.False(string.IsNullOrWhiteSpace(response.ChangedPathsUnavailable));
    }

    [Fact]
    public async Task an_unknown_reference_is_not_found_rather_than_a_fault()
    {
        var harness = new Harness();

        UseCaseResult<SnapshotComparisonResponse> result = await harness.RunAsync(
            new CompareSnapshotsUseCase(harness.Ports),
            new CompareSnapshotsRequest(TestData.Scope, TestData.Repository, "v1", "v9"));

        Assert.Equal(ExecutionOutcome.NotFound, result.Outcome);
        Assert.Contains("v9", result.Reason, StringComparison.Ordinal);
    }
}
