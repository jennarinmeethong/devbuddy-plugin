using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Analysis;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Analysis;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevBuddy.Security.Tests;

/// <summary>
/// Adapter failures, through the real pipeline, the real analyser and working-copy client, and the
/// real audit store in PostgreSQL.
/// <para>
/// Observed against v1.2.1 over MCP with a valid token on an AI-enabled project: a path escape
/// through <c>analyze_code</c>, and <c>analyze_change_impact</c> and <c>compare_snapshots</c> against
/// a repository with nothing mounted, each reached the client as a generic error and left no row in
/// <c>audit_events</c>. The guard held and nothing was read — but the refused escape, the event an
/// investigation into misuse most needs, was invisible, and an assistant could not tell its user to
/// mount the working copy. Each case here asserts the outcome and the row.
/// </para>
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class AdapterFailureAuditTests(SecurityFixture fixture) : IDisposable
{
    private readonly SecurityFixture _fixture = fixture;

    private readonly string _analysisRoot =
        Path.Combine(Path.GetTempPath(), "devbuddy-adapter-failures-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_analysisRoot))
        {
            Directory.Delete(_analysisRoot, recursive: true);
        }
    }

    [Fact]
    public async Task a_path_escape_through_analyze_code_is_denied_and_leaves_an_access_denied_row()
    {
        (World world, UserId user) = await AiContributorAsync();
        Directory.CreateDirectory(Path.Combine(_analysisRoot, world.AlphaId.Value.ToString()));

        using Session session = _fixture.OpenSession(world.Workspace);
        var useCase = new AnalyzeCodeUseCase(Analyzer(session));

        // The control: the same caller, the same project, a target inside the root succeeds. What
        // separates the two calls below is the guard, not the authorization service.
        UseCaseResult<AnalysisResponse> contained = await session.RunAsync(
            useCase, new AnalysisRequest(world.Alpha, Target: "src"), World.Ai(user));

        Assert.True(contained.IsSuccess, contained.Reason);

        UseCaseResult<AnalysisResponse> escaped = await session.RunAsync(
            useCase, new AnalysisRequest(world.Alpha, Target: "../../../etc"), World.Ai(user));

        Assert.Equal(ExecutionOutcome.Denied, escaped.Outcome);
        Assert.Contains("outside", escaped.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("../", escaped.Reason, StringComparison.Ordinal);

        AuditEventRow denied = Assert.Single(
            await RowsForAsync(session, world, user),
            row => row.Action == (int)AuditAction.AccessDenied);

        Assert.Equal((int)AuditOutcome.Denied, denied.Outcome);
        Assert.Equal("analyze_code:../../../etc", denied.ResourceReference);
        Assert.Equal("path-guard", denied.Details["refused_by"]);
    }

    [Fact]
    public async Task analyze_change_impact_with_no_working_copy_is_not_found_and_leaves_a_failed_row()
    {
        (World world, UserId user) = await AiContributorAsync();
        Directory.CreateDirectory(Path.Combine(_analysisRoot, world.AlphaId.Value.ToString()));

        using Session session = _fixture.OpenSession(world.Workspace);
        var repository = SourceRepositoryId.New();

        UseCaseResult<ChangeImpactResponse> result = await session.RunAsync(
            new AnalyzeChangeImpactUseCase(SourceSystem(session), Analyzer(session)),
            new AnalyzeChangeImpactRequest(world.Alpha, repository, "main"),
            World.Ai(user));

        Assert.Equal(ExecutionOutcome.NotFound, result.Outcome);
        Assert.Contains("No working copy is mounted", result.Reason, StringComparison.Ordinal);

        AuditEventRow row = Assert.Single(await RowsForAsync(session, world, user));
        Assert.Equal((int)AuditAction.AnalysisRun, row.Action);
        Assert.Equal((int)AuditOutcome.Failed, row.Outcome);
        Assert.Equal($"analyze_change_impact:{repository}@main", row.ResourceReference);
    }

    [Fact]
    public async Task compare_snapshots_with_no_working_copy_is_not_found_and_leaves_a_failed_row()
    {
        (World world, UserId user) = await AiContributorAsync();

        using Session session = _fixture.OpenSession(world.Workspace);
        var repository = SourceRepositoryId.New();

        UseCaseResult<SnapshotComparisonResponse> result = await session.RunAsync(
            new CompareSnapshotsUseCase(SourceSystem(session)),
            new CompareSnapshotsRequest(world.Alpha, repository, "main", "release"),
            World.Ai(user));

        Assert.Equal(ExecutionOutcome.NotFound, result.Outcome);
        Assert.Contains("No working copy is mounted", result.Reason, StringComparison.Ordinal);

        AuditEventRow row = Assert.Single(await RowsForAsync(session, world, user));
        Assert.Equal((int)AuditAction.RecordViewed, row.Action);
        Assert.Equal((int)AuditOutcome.Failed, row.Outcome);
        Assert.Equal($"compare_snapshots:{repository}", row.ResourceReference);
    }

    [Fact]
    public async Task an_installation_with_no_analysis_root_is_rejected_with_the_setting_named_and_leaves_a_failed_row()
    {
        (World world, UserId user) = await AiContributorAsync();

        using Session session = _fixture.OpenSession(world.Workspace);

        UseCaseResult<SnapshotComparisonResponse> result = await session.RunAsync(
            new CompareSnapshotsUseCase(SourceSystem(session, rootPath: string.Empty)),
            new CompareSnapshotsRequest(world.Alpha, SourceRepositoryId.New(), "main", "release"),
            World.Ai(user));

        Assert.Equal(ExecutionOutcome.Rejected, result.Outcome);
        Assert.Contains("Analysis:RootPath", result.Reason, StringComparison.Ordinal);

        AuditEventRow row = Assert.Single(await RowsForAsync(session, world, user));
        Assert.Equal((int)AuditOutcome.Failed, row.Outcome);
        Assert.Contains("Analysis:RootPath", row.Details["rejection"], StringComparison.Ordinal);
    }

    /// <summary>A Contributor on Alpha, which has AI access enabled: the observed configuration.</summary>
    private async Task<(World World, UserId User)> AiContributorAsync()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId user = await _fixture.CreateUserAsync($"adapter-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, user, Role.Contributor, world.AlphaId);
        await _fixture.EnableAiAccessAsync(world.Alpha, world.Founder);
        return (world, user);
    }

    private FileSystemCodeAnalyzer Analyzer(Session session) =>
        new FileSystemCodeAnalyzer(
            session.Resolve<IKnowledgeRepository>(),
            Options.Create(new AnalysisOptions { RootPath = _analysisRoot }));

    private WorkingCopySourceSystemClient SourceSystem(Session session, string? rootPath = null) =>
        new WorkingCopySourceSystemClient(
            Options.Create(new AnalysisOptions { RootPath = rootPath ?? _analysisRoot }),
            session.Resolve<IClock>());

    private static Task<List<AuditEventRow>> RowsForAsync(Session session, World world, UserId user) =>
        session.Db.AuditEvents
            .AsNoTracking()
            .Where(row => row.ProjectId == world.AlphaId.Value && row.ActorId == user.Value)
            .ToListAsync();
}
