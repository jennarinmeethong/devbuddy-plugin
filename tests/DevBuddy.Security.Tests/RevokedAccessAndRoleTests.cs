using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Work;

namespace DevBuddy.Security.Tests;

/// <summary>
/// Phase 11 scenario 2 (revoked permissions take effect), and the role table enforced end to end.
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class RevokedAccessAndRoleTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task revoking_a_grant_stops_the_very_next_request()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId reader = await _fixture.CreateUserAsync($"revoked-{Guid.NewGuid():N}@example.com");
        MembershipId grant = await _fixture.GrantAsync(world.Workspace, reader, Role.Viewer, world.AlphaId);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-R1", world.Founder);
        KnowledgeRecord record = await _fixture.SeedPublishedRecordAsync(
            world.Alpha, item.Id, "Alpha", "Readable while the grant lasts.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        var useCase = new GetRecordUseCase(session.Resolve<IKnowledgeRepository>());
        var request = new GetRecordRequest(world.Alpha, record.Id);

        Assert.True((await session.RunAsync(useCase, request, World.Human(reader))).IsSuccess);

        await _fixture.RevokeAsync(world.Workspace, grant);

        // Membership is read on every request rather than carried in the token, which is what
        // makes this the next request rather than the next sign-in (SB-11, SB-14).
        Assert.Equal(ExecutionOutcome.Denied,
            (await session.RunAsync(useCase, request, World.Human(reader))).Outcome);
    }

    [Fact]
    public async Task disabling_an_account_stops_access_without_waiting_for_the_token_to_expire()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId user = await _fixture.CreateUserAsync($"disabled-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, user, Role.Reviewer, world.AlphaId);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-R2", world.Founder);
        KnowledgeRecord record = await _fixture.SeedPublishedRecordAsync(
            world.Alpha, item.Id, "Alpha", "Body.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        var useCase = new GetRecordUseCase(session.Resolve<IKnowledgeRepository>());
        var request = new GetRecordRequest(world.Alpha, record.Id);

        Assert.True((await session.RunAsync(useCase, request, World.Human(user))).IsSuccess);

        await _fixture.DisableAccountAsync(user);

        // The access token this person holds is still perfectly valid and still cannot be used.
        // Checking the account state on every authorization is what closes that window.
        UseCaseResult<KnowledgeRecordView> denied =
            await session.RunAsync(useCase, request, World.Human(user));

        Assert.Equal(ExecutionOutcome.Denied, denied.Outcome);
        Assert.Contains("disabled", denied.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task a_viewer_cannot_draft_approve_publish_or_administer()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId viewer = await _fixture.CreateUserAsync($"viewer-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, viewer, Role.Viewer, world.AlphaId);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-R3", world.Founder);
        KnowledgeRecord record = await _fixture.SeedPublishedRecordAsync(
            world.Alpha, item.Id, "Alpha", "Body.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        var repository = session.Resolve<IKnowledgeRepository>();
        var clock = session.Resolve<IClock>();
        CallerContext caller = World.Human(viewer);

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new CreateDraftUseCase(repository, clock),
            new CreateDraftRequest(
                world.Alpha, item.Id, RecordKind.Handover, "Title", "Body",
                new Provenance(ProvenanceSourceKind.HumanAuthored, "test", "viewer", World.Now)),
            caller)).Outcome);

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new ApproveRecordUseCase(repository, clock),
            new ApproveRecordRequest(world.Alpha, record.Id, record.CurrentRevision.ContentHash.Value),
            caller)).Outcome);

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new PublishRecordUseCase(repository, clock),
            new RecordActionRequest(world.Alpha, record.Id), caller)).Outcome);

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new ExportProjectUseCase(session.Resolve<IAdministrativeOperations>()),
            new ExportProjectRequest(world.Alpha), caller)).Outcome);
    }

    [Fact]
    public async Task a_contributor_can_draft_but_cannot_approve_their_own_draft()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId contributor = await _fixture.CreateUserAsync($"contrib-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, contributor, Role.Contributor, world.AlphaId);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-R4", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        var repository = session.Resolve<IKnowledgeRepository>();
        var clock = session.Resolve<IClock>();
        CallerContext caller = World.Human(contributor);

        UseCaseResult<LifecycleResult> draft = await session.RunAsync(
            new CreateDraftUseCase(repository, clock),
            new CreateDraftRequest(
                world.Alpha, item.Id, RecordKind.Decision, "Their draft", "Their words.",
                new Provenance(ProvenanceSourceKind.HumanAuthored, "test", "contributor", World.Now)),
            caller);

        Assert.True(draft.IsSuccess);

        await session.RunAsync(
            new SubmitForApprovalUseCase(repository, clock),
            new RecordActionRequest(world.Alpha, draft.Value!.RecordId), caller);

        // info.md permits a draft creator to approve their own draft, subject to the same
        // permission check as any reviewer. A contributor does not pass that check, so
        // self-approval is refused here on the permission, not on the authorship.
        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new ApproveRecordUseCase(repository, clock),
            new ApproveRecordRequest(world.Alpha, draft.Value.RecordId, HashOf(session, world, draft.Value.RecordId)),
            caller)).Outcome);
    }

    [Fact]
    public async Task a_reviewer_may_approve_their_own_draft_and_the_audit_records_that_they_did()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId reviewer = await _fixture.CreateUserAsync($"self-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, reviewer, Role.Reviewer, world.AlphaId);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-R5", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        var repository = session.Resolve<IKnowledgeRepository>();
        var clock = session.Resolve<IClock>();
        CallerContext caller = World.Human(reviewer);

        UseCaseResult<LifecycleResult> draft = await session.RunAsync(
            new CreateDraftUseCase(repository, clock),
            new CreateDraftRequest(
                world.Alpha, item.Id, RecordKind.Decision, "Self approved", "Reviewed by its author.",
                new Provenance(ProvenanceSourceKind.HumanAuthored, "test", "reviewer", World.Now)),
            caller);

        await session.RunAsync(
            new SubmitForApprovalUseCase(repository, clock),
            new RecordActionRequest(world.Alpha, draft.Value!.RecordId), caller);

        Assert.True((await session.RunAsync(
            new ApproveRecordUseCase(repository, clock),
            new ApproveRecordRequest(world.Alpha, draft.Value.RecordId, HashOf(session, world, draft.Value.RecordId)),
            caller)).IsSuccess);

        KnowledgeRecord stored = await repository.FindRecordAsync(
            draft.Value.RecordId, world.Alpha, CancellationToken.None) ?? throw new InvalidOperationException();

        // Permitted, and visible. That combination is the requirement.
        Assert.True(Assert.Single(stored.Approvals).ApproverWasDraftCreator);
    }

    [Fact]
    public async Task only_an_administrator_reaches_the_audit_trail_and_access_management()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId reviewer = await _fixture.CreateUserAsync($"rev-{Guid.NewGuid():N}@example.com");
        UserId administrator = await _fixture.CreateUserAsync($"adm-{Guid.NewGuid():N}@example.com");

        await _fixture.GrantAsync(world.Workspace, reviewer, Role.Reviewer, world.AlphaId);
        await _fixture.GrantAsync(world.Workspace, administrator, Role.Administrator);

        using Session session = _fixture.OpenSession(world.Workspace);
        var useCase = new ReadAuditHistoryUseCase(session.Resolve<IAuditReader>());

        var request = new ReadAuditHistoryRequest(
            world.Alpha, World.Now.AddDays(-1), World.Now.AddDays(30));

        Assert.Equal(ExecutionOutcome.Denied,
            (await session.RunAsync(useCase, request, World.Human(reviewer))).Outcome);

        Assert.True((await session.RunAsync(useCase, request, World.Human(administrator))).IsSuccess);
    }

    [Fact]
    public async Task a_project_grant_does_not_reach_a_workspace_level_operation()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId projectAdmin = await _fixture.CreateUserAsync($"padm-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, projectAdmin, Role.Administrator, world.AlphaId);

        using Session session = _fixture.OpenSession(world.Workspace);

        // Administrator on one project is not administrator of the workspace. Granting membership
        // is a workspace act, and a project grant does not cover it.
        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new GrantMembershipUseCase(session.Resolve<IAccessDirectory>(), session.Resolve<IClock>()),
            new GrantMembershipRequest(world.Workspace, UserId.New(), Role.Viewer),
            World.Human(projectAdmin))).Outcome);
    }

    private static string HashOf(Session session, World world, KnowledgeRecordId recordId)
    {
        KnowledgeRecord record = session.Resolve<IKnowledgeRepository>()
            .FindRecordAsync(recordId, world.Alpha, CancellationToken.None)
            .GetAwaiter()
            .GetResult() ?? throw new InvalidOperationException("The record was not found.");

        return record.CurrentRevision.ContentHash.Value;
    }
}
