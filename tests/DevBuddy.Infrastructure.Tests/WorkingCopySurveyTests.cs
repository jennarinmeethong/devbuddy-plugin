using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Analysis;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// What the file surveys count as the project. Both defects here were found against v1.2.1 on a
/// synthetic working copy of eight files: the survey reported fifty-five because it walked
/// <c>.git</c>, and it labelled a 172 B file "large" because every one of the twenty largest source
/// files was labelled that way whatever its size.
/// </summary>
public sealed class WorkingCopySurveyTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "devbuddy-survey-" + Guid.NewGuid().ToString("N"));

    private readonly ProjectScope _scope = new(WorkspaceId.New(), ProjectId.New());

    public WorkingCopySurveyTests() => Directory.CreateDirectory(ProjectRoot);

    private string ProjectRoot => Path.Combine(_root, _scope.ProjectId.Value.ToString());

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task the_project_survey_counts_the_working_copy_and_not_its_git_directory()
    {
        WriteContent();
        WriteGitDirectory(ProjectRoot);

        AnalysisReport report = await Analyze(AnalysisKind.Project);

        Assert.StartsWith("3 files,", report.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(report.Observations, observation => observation.SourceLocator == ".sample");
        Assert.DoesNotContain(report.Observations, observation => observation.SourceLocator == "(none)");
    }

    [Theory]
    [InlineData(AnalysisKind.Code)]
    [InlineData(AnalysisKind.Documents)]
    [InlineData(AnalysisKind.Architecture)]
    [InlineData(AnalysisKind.TestEvidence)]
    public async Task no_survey_reports_a_file_from_inside_a_git_directory(AnalysisKind kind)
    {
        WriteContent();
        WriteGitDirectory(ProjectRoot);

        // A vendored repository keeps its own .git too, and that is no more content than the top one.
        string vendored = Path.Combine(ProjectRoot, "vendor", "library");
        Directory.CreateDirectory(vendored);
        WriteGitDirectory(vendored);

        AnalysisReport report = await Analyze(kind);

        // Every kind has something planted inside .git that it would otherwise have reported, so an
        // empty result here is the ignore list working and not a survey that found nothing to say.
        Assert.DoesNotContain(report.Observations, observation =>
            observation.SourceLocator.Split('/').Contains(".git", StringComparer.Ordinal));
    }

    [Fact]
    public async Task git_history_still_reads_the_directory_the_surveys_skip()
    {
        WriteContent();
        WriteGitDirectory(ProjectRoot);

        AnalysisReport report = await Analyze(AnalysisKind.GitHistory);

        Assert.Contains(report.Observations, observation =>
            observation.Subject == "head" && observation.Detail == "ref: refs/heads/main");
        Assert.Contains(report.Observations, observation =>
            observation.Subject == "ref" && observation.SourceLocator == "refs/heads/main");
    }

    [Fact]
    public async Task a_git_file_is_neither_counted_nor_mistaken_for_missing_metadata()
    {
        WriteContent();

        // What a worktree or a submodule checkout has in place of the directory.
        File.WriteAllText(Path.Combine(ProjectRoot, ".git"), "gitdir: /somewhere/else/.git/worktrees/copy\n");

        AnalysisReport project = await Analyze(AnalysisKind.Project);
        AnalysisReport history = await Analyze(AnalysisKind.GitHistory);

        Assert.StartsWith("3 files,", project.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(project.Observations, observation => observation.SourceLocator == ".git");
        Assert.Empty(history.Observations);
        Assert.Contains("kept outside", history.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task source_files_are_ranked_by_size_and_not_called_large()
    {
        File.WriteAllText(Path.Combine(ProjectRoot, "Small.cs"), new string('x', 172));
        File.WriteAllText(Path.Combine(ProjectRoot, "Bigger.cs"), new string('x', 4000));

        AnalysisReport report = await Analyze(AnalysisKind.Code);

        Assert.Equal(["Bigger.cs", "Small.cs"], report.Observations.Select(observation => observation.SourceLocator));
        Assert.All(report.Observations, observation => Assert.Equal("largest-source-file", observation.Subject));
        Assert.Equal("172 B", report.Observations[1].Detail);
    }

    /// <summary>Three files a person wrote: one source file, one document, one project file.</summary>
    private void WriteContent()
    {
        File.WriteAllText(Path.Combine(ProjectRoot, "Program.cs"), "class Program { }");
        File.WriteAllText(Path.Combine(ProjectRoot, "README.md"), "# Readme");
        File.WriteAllText(
            Path.Combine(ProjectRoot, "App.csproj"),
            "<Project><ItemGroup><PackageReference Include=\"Real.Package\" /></ItemGroup></Project>");
    }

    /// <summary>
    /// A .git directory holding something each survey would report if it walked in: hook samples
    /// and extensionless objects for the project survey, a script for code, a document, a project
    /// file for architecture, and a JSON file under a "test" path for test evidence.
    /// </summary>
    private static void WriteGitDirectory(string parent)
    {
        string git = Path.Combine(parent, ".git");
        Directory.CreateDirectory(Path.Combine(git, "hooks"));
        Directory.CreateDirectory(Path.Combine(git, "refs", "heads"));
        Directory.CreateDirectory(Path.Combine(git, "objects", "ab"));
        Directory.CreateDirectory(Path.Combine(git, "info", "test"));

        File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(git, "refs", "heads", "main"), "0123456789abcdef0123456789abcdef01234567\n");
        File.WriteAllText(Path.Combine(git, "objects", "ab", "cdef0123456789abcdef0123456789abcdef01"), "blob");
        File.WriteAllText(Path.Combine(git, "hooks", "pre-commit.sample"), "#!/bin/sh");
        File.WriteAllText(Path.Combine(git, "hooks", "hook.py"), "print('hook')");
        File.WriteAllText(Path.Combine(git, "info", "notes.md"), "# Not a project document");
        File.WriteAllText(
            Path.Combine(git, "info", "Planted.csproj"),
            "<Project><ItemGroup><PackageReference Include=\"Planted.Package\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(git, "info", "test", "results.json"), "{}");
    }

    private Task<AnalysisReport> Analyze(AnalysisKind kind) =>
        new FileSystemCodeAnalyzer(new UnusedRepository(), Options.Create(new AnalysisOptions { RootPath = _root }))
            .AnalyzeAsync(kind, _scope, null, null, CancellationToken.None);

    /// <summary>Every member throws: none of these surveys reads the database.</summary>
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
