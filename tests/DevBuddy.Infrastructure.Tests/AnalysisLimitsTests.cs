using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Analysis;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// Control SB-22 and the analysis half of SB-21: a deadline, a file-count ceiling, and a size
/// ceiling, all of them ending in a controlled result rather than an exception or a hang.
/// <para>
/// A repository under study is written by whoever can commit to it. Every one of these limits
/// exists because the alternative is a denial of service that anyone with commit access can
/// perform by adding files.
/// </para>
/// </summary>
public sealed class AnalysisLimitsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "devbuddy-limits-" + Guid.NewGuid().ToString("N"));

    private readonly ProjectScope _scope = new(WorkspaceId.New(), ProjectId.New());

    public AnalysisLimitsTests() => Directory.CreateDirectory(ProjectRoot);

    private string ProjectRoot => Path.Combine(_root, _scope.ProjectId.Value.ToString());

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task an_analysis_that_runs_out_of_time_returns_a_report_saying_so()
    {
        WriteFiles(50);

        AnalysisReport report = await Analyzer(options => options.Timeout = TimeSpan.Zero)
            .AnalyzeAsync(AnalysisKind.Project, _scope, null, null, CancellationToken.None);

        // A controlled failure, not an exception. The caller gets a result that says the answer is
        // incomplete and what to do about it, which is what a limit is for.
        Assert.Empty(report.Observations);
        Assert.Contains("stopped after", report.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("incomplete", report.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task a_caller_who_cancels_gets_a_cancellation_rather_than_a_report()
    {
        WriteFiles(50);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // The deadline and the caller are different things. Swallowing a caller cancellation into
        // a tidy report would hide the fact that nobody is waiting for the answer any more.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Analyzer().AnalyzeAsync(AnalysisKind.Project, _scope, null, null, cancelled.Token));
    }

    [Fact]
    public async Task the_file_ceiling_bounds_how_much_one_analysis_reads()
    {
        WriteFiles(60);

        AnalysisReport bounded = await Analyzer(options => options.MaxFilesVisited = 10)
            .AnalyzeAsync(AnalysisKind.Project, _scope, null, null, CancellationToken.None);

        AnalysisReport full = await Analyzer()
            .AnalyzeAsync(AnalysisKind.Project, _scope, null, null, CancellationToken.None);

        Assert.Contains("10 files", bounded.Summary, StringComparison.Ordinal);
        Assert.Contains("60 files", full.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_file_larger_than_the_size_ceiling_is_listed_but_not_read()
    {
        File.WriteAllText(Path.Combine(ProjectRoot, "small.md"), "# a heading");
        File.WriteAllText(Path.Combine(ProjectRoot, "huge.md"), new string('x', 5000));

        AnalysisReport report = await Analyzer(options => options.MaxFileBytes = 1000)
            .AnalyzeAsync(AnalysisKind.Documents, _scope, null, null, CancellationToken.None);

        // Both are counted. Only the one within the ceiling has its contents read, so a giant file
        // cannot be used to pull memory out of the process.
        Assert.Contains("2 documents", report.Summary, StringComparison.Ordinal);
        Assert.Contains(report.Observations, observation => observation.SourceLocator == "small.md");
        Assert.DoesNotContain(report.Observations, observation => observation.SourceLocator == "huge.md");
    }

    [Fact]
    public async Task a_deeply_nested_tree_still_finishes()
    {
        string path = ProjectRoot;

        for (int depth = 0; depth < 60; depth++)
        {
            path = Path.Combine(path, $"level{depth}");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "file.cs"), "class X { }");
        }

        AnalysisReport report = await Analyzer(options => options.Timeout = TimeSpan.FromSeconds(30))
            .AnalyzeAsync(AnalysisKind.Code, _scope, null, null, CancellationToken.None);

        // Depth is bounded by the same file ceiling as breadth, because the walk is iterative
        // rather than recursive: a deep tree cannot exhaust the stack.
        Assert.Contains("60 source files", report.Summary, StringComparison.Ordinal);
    }

    private void WriteFiles(int count)
    {
        for (int index = 0; index < count; index++)
        {
            File.WriteAllText(Path.Combine(ProjectRoot, $"file{index}.cs"), "class X { }");
        }
    }

    private FileSystemCodeAnalyzer Analyzer(Action<AnalysisOptions>? configure = null)
    {
        var options = new AnalysisOptions { RootPath = _root };
        configure?.Invoke(options);

        return new FileSystemCodeAnalyzer(new UnusedRepository(), Options.Create(options));
    }

    /// <summary>Every member throws, so a test that started depending on one would say so.</summary>
    private sealed class UnusedRepository : IKnowledgeRepository
    {
        public Task<KnowledgeRecord?> FindRecordAsync(
            KnowledgeRecordId id, ProjectScope scope, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<KnowledgeRecord>> ListRecordsForWorkItemAsync(
            WorkItemId workItemId, ProjectScope scope, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkItem?> FindWorkItemAsync(
            WorkItemId id, ProjectScope scope, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkItem>> ListWorkItemsAsync(
            ProjectScope scope, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<KnowledgeRecord>> ListRecordsAsync(
            ProjectScope scope, IReadOnlyList<RecordStatus>? statuses, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddWorkItemAsync(WorkItem workItem, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddRecordAsync(KnowledgeRecord record, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateRecordAsync(KnowledgeRecord record, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
