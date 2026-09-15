using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Analysis;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// What a set of changed paths touches in a working copy.
/// <para>
/// Until 2026-09-15 <c>analyze_change_impact</c> joined its changed paths with semicolons and
/// asked the analyser about that one string as if it were a path, so every call answered no
/// impact. These tests hand the analyser a list and check what it says about each entry.
/// </para>
/// </summary>
public sealed class ChangeImpactAnalysisTests : IDisposable
{
    private const string NoProject = "No project or package manifest encloses this path.";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "devbuddy-impact-" + Guid.NewGuid().ToString("N"));

    private readonly ProjectScope _scope = new(WorkspaceId.New(), ProjectId.New());
    private readonly SourceRepositoryId _repository = SourceRepositoryId.New();

    public ChangeImpactAnalysisTests()
    {
        Write("src/Bms.Core/Bms.Core.csproj", Project());
        Write("src/Bms.Core/ImportJob.cs", "class ImportJob { }");

        // A reference that climbs out of the working copy sits beside a real one.
        Write("src/Bms.Api/Bms.Api.csproj", Project(@"..\Bms.Core\Bms.Core.csproj", @"..\..\..\..\..\etc\passwd"));
        Write("src/Bms.Worker/Bms.Worker.csproj", Project(@"..\Bms.Api\Bms.Api.csproj"));
        Write("src/Attestation/Signer.cs", "class Signer { }");
        Write("tests/Bms.Core.Tests/Bms.Core.Tests.csproj", Project(@"..\..\src\Bms.Core\Bms.Core.csproj"));
        Write("tests/Bms.Core.Tests/ImportJobTests.cs", "class ImportJobTests { }");
        Write("docs/import.md", "# Import");
        Write("test-results/junit.xml", "<testsuite />");
        Write("api/openapi.yaml", "openapi: 3.1.0");
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
    public async Task a_changed_source_file_maps_to_its_project_and_the_projects_that_depend_on_it()
    {
        IReadOnlyList<AnalysisObservation> impact = await Analyzer()
            .AnalyzeChangedPathsAsync(_scope, _repository, ["src/Bms.Core/ImportJob.cs"], Ct);

        Assert.Contains(
            new AnalysisObservation("project", "src/Bms.Core/Bms.Core.csproj", "src/Bms.Core/ImportJob.cs"), impact);
        Assert.Contains(
            new AnalysisObservation("dependent-project", "src/Bms.Api/Bms.Api.csproj", "src/Bms.Core/Bms.Core.csproj"), impact);
        Assert.Contains(
            new AnalysisObservation(
                "dependent-project", "tests/Bms.Core.Tests/Bms.Core.Tests.csproj", "src/Bms.Core/Bms.Core.csproj"),
            impact);

        // Reached through Bms.Api rather than directly.
        Assert.Contains(
            new AnalysisObservation("dependent-project", "src/Bms.Worker/Bms.Worker.csproj", "src/Bms.Api/Bms.Api.csproj"), impact);

        Assert.DoesNotContain(impact, observation => observation.Subject is "test" or "no-project" or "path-refused");
        Assert.DoesNotContain(impact, observation =>
            observation.Detail.Contains("passwd", StringComparison.Ordinal)
            || observation.SourceLocator.Contains("passwd", StringComparison.Ordinal));
    }

    [Fact]
    public async Task tests_test_results_documents_and_contracts_are_named_as_such()
    {
        IReadOnlyList<AnalysisObservation> impact = await Analyzer().AnalyzeChangedPathsAsync(
            _scope,
            _repository,
            [
                "tests/Bms.Core.Tests/ImportJobTests.cs",
                "test-results/junit.xml",
                "docs/import.md",
                "api/openapi.yaml",
                "src/Attestation/Signer.cs",
            ],
            Ct);

        Assert.Equal(["tests/Bms.Core.Tests/ImportJobTests.cs"], Located(impact, "test"));
        Assert.Equal(["test-results/junit.xml"], Located(impact, "test-evidence"));
        Assert.Equal(["docs/import.md"], Located(impact, "document"));
        Assert.Equal(["api/openapi.yaml"], Located(impact, "api-contract"));

        Assert.Contains(
            new AnalysisObservation(
                "project", "tests/Bms.Core.Tests/Bms.Core.Tests.csproj", "tests/Bms.Core.Tests/ImportJobTests.cs"),
            impact);

        // "Attestation" contains "test"; a substring rule would have called this a test.
        Assert.Contains(new AnalysisObservation("no-project", NoProject, "src/Attestation/Signer.cs"), impact);
    }

    [Fact]
    public async Task a_path_the_change_deleted_is_mapped_where_it_can_be_and_never_throws()
    {
        Assert.False(Directory.Exists(Path.Combine(WorkingCopy, "src", "Gone")));

        IReadOnlyList<AnalysisObservation> impact = await Analyzer().AnalyzeChangedPathsAsync(
            _scope, _repository, ["src/Bms.Core/Removed.cs", "src/Gone/Old.cs"], Ct);

        // The file is gone and its project is not.
        Assert.Contains(
            new AnalysisObservation("project", "src/Bms.Core/Bms.Core.csproj", "src/Bms.Core/Removed.cs"), impact);

        // The file and its directory are both gone, and nothing above them is a project.
        Assert.Contains(new AnalysisObservation("no-project", NoProject, "src/Gone/Old.cs"), impact);
    }

    [Fact]
    public async Task a_changed_path_that_climbs_out_of_the_working_copy_is_refused_and_not_read()
    {
        IReadOnlyList<AnalysisObservation> impact = await Analyzer()
            .AnalyzeChangedPathsAsync(_scope, _repository, ["../../outside.cs"], Ct);

        AnalysisObservation refused = Assert.Single(impact);
        Assert.Equal("path-refused", refused.Subject);
        Assert.Equal("../../outside.cs", refused.SourceLocator);
    }

    [Fact]
    public async Task without_a_working_copy_paths_are_still_classified_and_the_gap_is_said()
    {
        SourceRepositoryId unmounted = SourceRepositoryId.New();

        IReadOnlyList<AnalysisObservation> impact = await Analyzer()
            .AnalyzeChangedPathsAsync(_scope, unmounted, ["tests/Bms.Core.Tests/ImportJobTests.cs"], Ct);

        Assert.Contains(impact, observation => observation.Subject == "test");
        AnalysisObservation gap = Assert.Single(impact, observation => observation.Subject == "working-copy");
        Assert.Equal(unmounted.ToString(), gap.SourceLocator);
        Assert.DoesNotContain(impact, observation => observation.Subject is "project" or "no-project");
    }

    [Fact]
    public async Task a_change_that_runs_out_of_time_says_its_impact_is_incomplete()
    {
        IReadOnlyList<AnalysisObservation> impact = await Analyzer(TimeSpan.Zero)
            .AnalyzeChangedPathsAsync(_scope, _repository, ["src/Bms.Core/ImportJob.cs"], Ct);

        Assert.Contains(impact, observation => observation.Subject == "incomplete");
        Assert.DoesNotContain(impact, observation => observation.Subject == "project");
    }

    private FileSystemCodeAnalyzer Analyzer(TimeSpan? timeout = null)
    {
        var options = new AnalysisOptions { RootPath = _root };

        if (timeout is { } limit)
        {
            options.Timeout = limit;
        }

        return new FileSystemCodeAnalyzer(new UnusedKnowledgeRepository(), Options.Create(options));
    }

    private static string[] Located(IEnumerable<AnalysisObservation> impact, string subject) =>
        [.. impact.Where(observation => observation.Subject == subject).Select(observation => observation.SourceLocator)];

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(WorkingCopy, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Project(params string[] references) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n"
        + string.Concat(references.Select(reference => $"    <ProjectReference Include=\"{reference}\" />\n"))
        + "  </ItemGroup>\n</Project>\n";

    /// <summary>Changed paths are mapped from the working copy alone; a member reached here would say so.</summary>
    private sealed class UnusedKnowledgeRepository : IKnowledgeRepository
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
