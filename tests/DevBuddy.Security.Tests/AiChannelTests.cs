using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Application.UseCases.Analysis;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Work;

namespace DevBuddy.Security.Tests;

/// <summary>
/// The AI data policy, enforced rather than described: denied by default per project, opt-in by
/// the project owner, narrowed to the requesting user, and never reaching a human-gated operation
/// (SB-08, SB-09, SB-10).
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class AiChannelTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task ai_is_denied_on_a_project_whose_owner_never_enabled_it()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId user = await _fixture.CreateUserAsync($"ai-off-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, user, Role.Reviewer, world.AlphaId);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-AI1", world.Founder);
        KnowledgeRecord record = await _fixture.SeedPublishedRecordAsync(
            world.Alpha, item.Id, "Alpha", "Body.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        var useCase = new GetRecordUseCase(session.Resolve<IKnowledgeRepository>());
        var request = new GetRecordRequest(world.Alpha, record.Id);

        // The same person, the same permission, the same record. Only the channel differs.
        Assert.True((await session.RunAsync(useCase, request, World.Human(user))).IsSuccess);

        UseCaseResult<KnowledgeRecordView> denied =
            await session.RunAsync(useCase, request, World.Ai(user));

        Assert.Equal(ExecutionOutcome.Denied, denied.Outcome);
        Assert.Contains("AI access is not enabled", denied.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ai_reaches_a_project_once_the_owner_enables_it()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId user = await _fixture.CreateUserAsync($"ai-on-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, user, Role.Reviewer, world.AlphaId);
        await _fixture.EnableAiAccessAsync(world.Alpha, world.Founder);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-AI2", world.Founder);
        KnowledgeRecord record = await _fixture.SeedPublishedRecordAsync(
            world.Alpha, item.Id, "Alpha", "Body.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);

        Assert.True((await session.RunAsync(
            new GetRecordUseCase(session.Resolve<IKnowledgeRepository>()),
            new GetRecordRequest(world.Alpha, record.Id),
            World.Ai(user))).IsSuccess);
    }

    [Fact]
    public async Task enabling_one_project_does_not_open_the_one_next_to_it()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId user = await _fixture.CreateUserAsync($"ai-one-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, user, Role.Reviewer);
        await _fixture.EnableAiAccessAsync(world.Alpha, world.Founder);

        WorkItem betaItem = await _fixture.SeedWorkItemAsync(world.Beta, "CRQ-AI3", world.Founder);
        KnowledgeRecord betaRecord = await _fixture.SeedPublishedRecordAsync(
            world.Beta, betaItem.Id, "Beta", "Body.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);

        // A workspace-wide grant, and the person can read Beta themselves. AI still cannot,
        // because the opt-in is per project.
        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new GetRecordUseCase(session.Resolve<IKnowledgeRepository>()),
            new GetRecordRequest(world.Beta, betaRecord.Id),
            World.Ai(user))).Outcome);
    }

    [Fact]
    public async Task listing_projects_over_the_ai_channel_hides_the_ones_that_are_not_enabled()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId user = await _fixture.CreateUserAsync($"ai-list-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, user, Role.Viewer);
        await _fixture.EnableAiAccessAsync(world.Alpha, world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);

        var useCase = new ListProjectsUseCase(
            session.Resolve<IProjectDirectory>(), session.Resolve<IAccessDirectory>());

        var request = new ListProjectsRequest(world.Workspace);

        UseCaseResult<ListProjectsResponse> asHuman =
            await session.RunAsync(useCase, request, World.Human(user));

        UseCaseResult<ListProjectsResponse> asAi =
            await session.RunAsync(useCase, request, World.Ai(user));

        // The person sees both projects. AI sees only the one that was opened to it, because
        // naming a project is itself a disclosure.
        Assert.Equal(2, asHuman.Value!.Projects.Count);
        Assert.Equal(world.AlphaId, Assert.Single(asAi.Value!.Projects).ProjectId);
    }

    [Fact]
    public async Task ai_cannot_reach_a_human_gated_operation_even_on_an_enabled_project()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId reviewer = await _fixture.CreateUserAsync($"ai-gate-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, reviewer, Role.Administrator);
        await _fixture.EnableAiAccessAsync(world.Alpha, world.Founder);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-AI4", world.Founder);
        KnowledgeRecord record = await _fixture.SeedPublishedRecordAsync(
            world.Alpha, item.Id, "Alpha", "Body.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        var repository = session.Resolve<IKnowledgeRepository>();
        var clock = session.Resolve<IClock>();

        // An administrator, on a project with AI enabled, over the AI channel. Still refused,
        // and refused structurally: the descriptor says Denied, so the pipeline stops before
        // authorization is even consulted (SB-10).
        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new ApproveRecordUseCase(repository, clock),
            new ApproveRecordRequest(world.Alpha, record.Id, record.CurrentRevision.ContentHash.Value),
            World.Ai(reviewer))).Outcome);

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new PublishRecordUseCase(repository, clock),
            new RecordActionRequest(world.Alpha, record.Id),
            World.Ai(reviewer))).Outcome);
    }

    [Fact]
    public async Task ai_cannot_download_an_attachment()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId user = await _fixture.CreateUserAsync($"ai-att-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, user, Role.Administrator);
        await _fixture.EnableAiAccessAsync(world.Alpha, world.Founder);

        EvidenceObject evidence;

        using (Session seeding = _fixture.OpenSession(world.Workspace))
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("raw log"));

            evidence = await seeding.Resolve<IEvidenceStore>()
                .StoreAsync(world.Alpha, stream, "text/plain", world.Founder, CancellationToken.None);
        }

        using Session session = _fixture.OpenSession(world.Workspace);

        // info.md permits AI to search, get, analyse, draft, and generate a handover. A raw log is
        // none of those, and it is the material most likely to carry something a scanner missed.
        UseCaseResult<EvidenceDownloadResponse> denied = await session.RunAsync(
            new DownloadEvidenceUseCase(session.Resolve<IEvidenceStore>()),
            new DownloadEvidenceRequest(world.Alpha, evidence.Id),
            World.Ai(user));

        Assert.Equal(ExecutionOutcome.Denied, denied.Outcome);
        Assert.Contains("download_evidence", denied.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task disabling_ai_access_again_closes_the_project()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId user = await _fixture.CreateUserAsync($"ai-off2-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, user, Role.Administrator, world.AlphaId);
        await _fixture.EnableAiAccessAsync(world.Alpha, world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        var analysis = new AnalyzeProjectUseCase(new StubAnalyzer());
        var request = new AnalysisRequest(world.Alpha);

        Assert.True((await session.RunAsync(analysis, request, World.Ai(user))).IsSuccess);

        await session.RunAsync(
            new DisableProjectAiAccessUseCase(session.Resolve<IAccessDirectory>()),
            new DisableProjectAiAccessRequest(world.Alpha),
            World.Human(user));

        Assert.Equal(ExecutionOutcome.Denied,
            (await session.RunAsync(analysis, request, World.Ai(user))).Outcome);
    }

    /// <summary>
    /// Stands in for the Phase 6 analyser. These tests are about who may run an analysis, not
    /// about what one produces.
    /// </summary>
    private sealed class StubAnalyzer : ICodeAnalyzer
    {
        public Task<AnalysisReport> AnalyzeAsync(
            AnalysisKind kind,
            Domain.Tenancy.ProjectScope scope,
            SourceRepositoryId? repositoryId,
            string? target,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AnalysisReport(kind, "stub", []));
    }
}
