using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Analysis;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Tests;

namespace DevBuddy.Security.Tests;

/// <summary>
/// <c>analyze_change_impact</c> through the real pipeline, over a working copy of real git objects
/// and records in real PostgreSQL.
/// <para>
/// This is what was observed on 2026-09-15 against v1.2.1: the changed paths were right and the
/// impact was empty, even with a published record citing the changed file at the tag. It is also
/// SB-26 for this operation: a draft citing the same file does not appear, and neither does a
/// record in the project next door.
/// </para>
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class ChangeImpactTests(SecurityFixture fixture)
{
    private const string ImportJob = "src/Bms.Core/ImportJob.cs";
    private const string CoreProject = "src/Bms.Core/Bms.Core.csproj";

    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task a_tagged_range_reports_the_project_and_only_the_published_record_citing_the_changed_file()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId user = await _fixture.CreateUserAsync($"impact-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, user, Role.Contributor, world.AlphaId);

        SourceRepositoryId repository = SourceRepositoryId.New();
        string head = WriteRepository(world.Alpha, repository);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-IMP1", world.Founder);
        KnowledgeRecord published = await AddAsync(
            Record(world.Alpha, item.Id, $"{ImportJob}@v0.2.0", world.Founder, publish: true));
        KnowledgeRecord draft = await AddAsync(
            Record(world.Alpha, item.Id, $"{ImportJob}@v0.2.0", world.Founder, publish: false));

        WorkItem betaItem = await _fixture.SeedWorkItemAsync(world.Beta, "CRQ-IMP2", world.Founder);
        KnowledgeRecord elsewhere = await AddAsync(
            Record(world.Beta, betaItem.Id, ImportJob, world.Founder, publish: true));

        using Session session = _fixture.OpenSession(world.Workspace);
        var useCase = new AnalyzeChangeImpactUseCase(
            session.Resolve<ISourceSystemClient>(),
            session.Resolve<ICodeAnalyzer>(),
            session.Resolve<IKnowledgeRepository>());

        // The two calls that were observed: the tag range, and the single commit.
        foreach (string commitOrRange in new[] { "refs/tags/v0.1.0..refs/tags/v0.2.0", head })
        {
            UseCaseResult<ChangeImpactResponse> result = await session.RunAsync(
                useCase, new AnalyzeChangeImpactRequest(world.Alpha, repository, commitOrRange), World.Human(user));

            Assert.True(result.IsSuccess, result.Reason);
            ChangeImpactResponse response = result.Value!;

            Assert.Equal(2, response.ChangedPaths.Count);
            Assert.Contains(ImportJob, response.ChangedPaths);
            Assert.Contains("test-results/junit.xml", response.ChangedPaths);

            Assert.Contains(new AnalysisObservation("project", CoreProject, ImportJob), response.Impact);
            Assert.Contains(
                new AnalysisObservation("dependent-project", "src/Bms.Api/Bms.Api.csproj", CoreProject), response.Impact);
            Assert.Contains(response.Impact, observation =>
                observation.Subject == "test-evidence" && observation.SourceLocator == "test-results/junit.xml");

            AnalysisObservation cited = Assert.Single(
                response.Impact, observation => observation.Subject == "knowledge-record");

            Assert.Equal(ImportJob, cited.SourceLocator);
            Assert.Contains(published.Id.ToString(), cited.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(draft.Id.ToString(), cited.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(elsewhere.Id.ToString(), cited.Detail, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Two commits tagged v0.1.0 and v0.2.0, and the working copy checked out at the second, which
    /// is what an operator mounts.
    /// </summary>
    private string WriteRepository(ProjectScope scope, SourceRepositoryId repository)
    {
        string workingCopy = Path.Combine(
            _fixture.AnalysisRoot, scope.ProjectId.Value.ToString(), repository.Value.ToString());

        Directory.CreateDirectory(workingCopy);
        var git = new GitFixture(workingCopy);

        var before = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["README.md"] = "# Bms",
            [CoreProject] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            [ImportJob] = "class ImportJob { }",
            ["src/Bms.Api/Bms.Api.csproj"] =
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n"
                + "    <ProjectReference Include=\"..\\Bms.Core\\Bms.Core.csproj\" />\n"
                + "  </ItemGroup>\n</Project>\n",
        };

        var after = new Dictionary<string, string>(before, StringComparer.Ordinal)
        {
            [ImportJob] = "class ImportJob { void Normalise() { } }",
            ["test-results/junit.xml"] = "<testsuite tests=\"1\" />",
        };

        string first = git.Commit(before);
        string second = git.Commit(after, parent: first);

        git.SetTag("v0.1.0", first);
        git.SetTag("v0.2.0", second);
        git.SetBranch("main", second);

        foreach ((string path, string content) in after)
        {
            string file = Path.Combine(workingCopy, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        return second;
    }

    private async Task<KnowledgeRecord> AddAsync(KnowledgeRecord record)
    {
        using Session session = _fixture.OpenSession(record.Scope.WorkspaceId);
        await session.Resolve<IKnowledgeRepository>().AddRecordAsync(record, CancellationToken.None);
        return record;
    }

    private static KnowledgeRecord Record(
        ProjectScope scope, WorkItemId workItemId, string sourceLocator, UserId author, bool publish)
    {
        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), scope, workItemId, RecordKind.Decision,
            "Import job normalisation", "Identifiers are normalised before validation.",
            frontMatter: null,
            new Provenance(ProvenanceSourceKind.HumanAuthored, sourceLocator, "seed", World.Now),
            World.Now, author);

        if (publish)
        {
            record.SubmitForApproval(World.Now.AddMinutes(1));
            record.Approve(author, record.CurrentRevision.ContentHash, World.Now.AddMinutes(2));
            record.Publish(World.Now.AddMinutes(3));
        }

        return record;
    }
}
