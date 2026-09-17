using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Security.Tests;

/// <summary>
/// The project in a scope is a claim too (SB-11). Found by the end-to-end suite on 2026-09-17: a
/// membership in the named workspace was checked, but nothing checked that the project belonged
/// to that workspace, or existed. Reads leaked nothing because the tenant filter answered with an
/// empty list; writes were accepted, and a work item was stored under the caller's workspace
/// against another tenant's project identifier, or against one that exists nowhere.
/// <para>
/// The refusals use a workspace-wide Administrator, the strongest grant there is, so the only thing
/// that can refuse is the project check itself. The last case covers <c>scope-report</c>, which
/// lists what an installation may have kept from before the check.
/// </para>
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class ProjectScopeTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task a_work_item_cannot_be_written_against_another_workspaces_project()
    {
        World ours = await _fixture.CreateWorldAsync();
        World theirs = await _fixture.CreateWorldAsync();
        UserId administrator = await AdministratorOfAsync(ours);

        var stray = new ProjectScope(ours.Workspace, theirs.AlphaId);

        UseCaseResult<WorkItemCreatedResponse> result = await CreateWorkItemAsync(ours, stray, administrator);

        Assert.Equal(ExecutionOutcome.Denied, result.Outcome);
        Assert.False(await AnyWorkItemAgainstAsync(theirs.AlphaId));
    }

    [Fact]
    public async Task a_work_item_cannot_be_written_against_a_project_that_does_not_exist()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId administrator = await AdministratorOfAsync(world);
        var ghost = ProjectId.New();

        UseCaseResult<WorkItemCreatedResponse> result =
            await CreateWorkItemAsync(world, new ProjectScope(world.Workspace, ghost), administrator);

        Assert.Equal(ExecutionOutcome.Denied, result.Outcome);
        Assert.False(await AnyWorkItemAgainstAsync(ghost));
    }

    [Fact]
    public async Task reading_another_workspaces_project_is_refused_rather_than_answered_empty()
    {
        World ours = await _fixture.CreateWorldAsync();
        World theirs = await _fixture.CreateWorldAsync();
        UserId administrator = await AdministratorOfAsync(ours);
        await _fixture.SeedWorkItemAsync(theirs.Alpha, "CRQ-FOREIGN", theirs.Founder);

        var stray = new ProjectScope(ours.Workspace, theirs.AlphaId);

        using Session session = _fixture.OpenSession(ours.Workspace);

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new ListWorkItemsUseCase(session.Resolve<IKnowledgeRepository>()),
            new ListWorkItemsRequest(stray),
            World.Human(administrator))).Outcome);

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new SearchKnowledgeUseCase(session.Resolve<ISearchIndex>()),
            new SearchKnowledgeRequest(stray, "anything"),
            World.Human(administrator))).Outcome);
    }

    [Fact]
    public async Task a_deleted_project_is_refused_rather_than_answered_empty()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId administrator = await AdministratorOfAsync(world);

        using Session session = _fixture.OpenSession(world.Workspace);

        Assert.True((await session.RunAsync(
            new DeleteProjectUseCase(session.Resolve<IProjectDirectory>()),
            new DeleteProjectRequest(world.Alpha),
            World.Human(administrator))).IsSuccess);

        Assert.Equal(ExecutionOutcome.Denied, (await session.RunAsync(
            new ListWorkItemsUseCase(session.Resolve<IKnowledgeRepository>()),
            new ListWorkItemsRequest(world.Alpha),
            World.Human(administrator))).Outcome);

        // The project next to it is untouched, which is what shows the refusal is about the
        // deleted project and not about the caller.
        Assert.True((await session.RunAsync(
            new ListWorkItemsUseCase(session.Resolve<IKnowledgeRepository>()),
            new ListWorkItemsRequest(world.Beta),
            World.Human(administrator))).IsSuccess);
    }

    [Fact]
    public async Task the_refusal_does_not_say_whether_the_project_exists()
    {
        World ours = await _fixture.CreateWorldAsync();
        World theirs = await _fixture.CreateWorldAsync();

        // Scoped to Alpha, so Beta is a real project of this workspace the caller cannot reach.
        UserId scoped = await _fixture.CreateUserAsync($"scoped-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(ours.Workspace, scoped, Role.Viewer, ours.AlphaId);

        UseCaseResult<ListWorkItemsResponse>[] refusals =
        [
            await ListAsync(ours, ours.Beta, scoped),
            await ListAsync(ours, new ProjectScope(ours.Workspace, theirs.AlphaId), scoped),
            await ListAsync(ours, new ProjectScope(ours.Workspace, ProjectId.New()), scoped),
        ];

        Assert.All(refusals, refusal => Assert.Equal(ExecutionOutcome.Denied, refusal.Outcome));
        Assert.Single(refusals.Select(refusal => refusal.Reason).Distinct());
    }

    [Fact]
    public async Task a_live_project_of_the_workspace_is_still_reached()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId administrator = await AdministratorOfAsync(world);

        UseCaseResult<WorkItemCreatedResponse> result = await CreateWorkItemAsync(world, world.Alpha, administrator);

        Assert.True(result.IsSuccess, $"Expected success but got {result.Outcome}: {result.Reason}");
    }

    [Fact]
    public async Task a_project_grant_cannot_name_another_workspaces_project()
    {
        World ours = await _fixture.CreateWorldAsync();
        World theirs = await _fixture.CreateWorldAsync();
        UserId administrator = await AdministratorOfAsync(ours);
        UserId subject = await _fixture.CreateUserAsync($"subject-{Guid.NewGuid():N}@example.com");

        using Session session = _fixture.OpenSession(ours.Workspace);
        var useCase = new GrantMembershipUseCase(
            session.Resolve<IAccessDirectory>(), session.Resolve<IProjectDirectory>(), session.Resolve<IClock>());

        foreach (ProjectId project in new[] { theirs.AlphaId, ProjectId.New() })
        {
            UseCaseResult<MembershipResponse> result = await session.RunAsync(
                useCase,
                new GrantMembershipRequest(ours.Workspace, subject, Role.Administrator, project),
                World.Human(administrator));

            Assert.Equal(ExecutionOutcome.NotFound, result.Outcome);
        }

        Assert.False(await session.Db.Memberships
            .IgnoreQueryFilters()
            .AnyAsync(row => row.UserId == subject.Value));

        // And the same grant on a project this workspace does have is made.
        Assert.True((await session.RunAsync(
            useCase,
            new GrantMembershipRequest(ours.Workspace, subject, Role.Viewer, ours.AlphaId),
            World.Human(administrator))).IsSuccess);
    }

    [Fact]
    public async Task the_scope_report_lists_rows_written_before_the_check_and_changes_nothing()
    {
        World ours = await _fixture.CreateWorldAsync();
        World theirs = await _fixture.CreateWorldAsync();
        var ghost = ProjectId.New();
        UserId member = await _fixture.CreateUserAsync($"stray-{Guid.NewGuid():N}@example.com");

        // Planted around the pipeline, because the pipeline no longer writes them: this is what an
        // installation may have kept from before 2026-09-17.
        await _fixture.SeedWorkItemAsync(new ProjectScope(ours.Workspace, theirs.AlphaId), "CRQ-STRAY1", ours.Founder);
        await _fixture.SeedWorkItemAsync(new ProjectScope(ours.Workspace, ghost), "CRQ-STRAY2", ours.Founder);
        await _fixture.SeedWorkItemAsync(new ProjectScope(ours.Workspace, ghost), "CRQ-STRAY3", ours.Founder);
        await _fixture.GrantAsync(ours.Workspace, member, Role.Viewer, ghost);

        // Not strays: a live project, and a workspace-wide grant, which names no project at all.
        await _fixture.SeedWorkItemAsync(ours.Alpha, "CRQ-FINE", ours.Founder);
        await _fixture.GrantAsync(ours.Workspace, member, Role.Viewer);

        using Session session = _fixture.OpenUnscopedSession();

        IReadOnlyList<StrayScopeRows> found = (await session.Resolve<IScopeIntegrityReport>()
            .FindAsync(TestToken.None))
            .Where(entry => entry.WorkspaceId == ours.Workspace.Value)
            .OrderBy(entry => entry.Table, StringComparer.Ordinal)
            .ThenBy(entry => entry.Rows)
            .ToList();

        Assert.Equal(
            [
                new StrayScopeRows("memberships", ours.Workspace.Value, ghost.Value, 1),
                new StrayScopeRows("work_items", ours.Workspace.Value, theirs.AlphaId.Value, 1),
                new StrayScopeRows("work_items", ours.Workspace.Value, ghost.Value, 2),
            ],
            found);

        // Their project is untouched and does not appear under its own workspace either.
        Assert.DoesNotContain(
            await session.Resolve<IScopeIntegrityReport>().FindAsync(TestToken.None),
            entry => entry.WorkspaceId == theirs.Workspace.Value);

        // Reporting deleted nothing.
        Assert.True(await AnyWorkItemAgainstAsync(ghost));
    }

    private async Task<UserId> AdministratorOfAsync(World world)
    {
        UserId administrator = await _fixture.CreateUserAsync($"ws-admin-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, administrator, Role.Administrator);
        return administrator;
    }

    private async Task<UseCaseResult<WorkItemCreatedResponse>> CreateWorkItemAsync(
        World world, ProjectScope scope, UserId caller)
    {
        using Session session = _fixture.OpenSession(world.Workspace);

        return await session.RunAsync(
            new CreateWorkItemUseCase(session.Resolve<IKnowledgeRepository>(), session.Resolve<IClock>()),
            new CreateWorkItemRequest(
                scope, $"STRAY-{Guid.NewGuid():N}"[..12], WorkItemType.Develop, "Stray", "Should be refused."),
            World.Human(caller));
    }

    private async Task<UseCaseResult<ListWorkItemsResponse>> ListAsync(
        World world, ProjectScope scope, UserId caller)
    {
        using Session session = _fixture.OpenSession(world.Workspace);

        return await session.RunAsync(
            new ListWorkItemsUseCase(session.Resolve<IKnowledgeRepository>()),
            new ListWorkItemsRequest(scope),
            World.Human(caller));
    }

    private async Task<bool> AnyWorkItemAgainstAsync(ProjectId project)
    {
        using Session session = _fixture.OpenUnscopedSession();

        return await session.Db.WorkItems
            .IgnoreQueryFilters()
            .AnyAsync(row => row.ProjectId == project.Value);
    }
}
