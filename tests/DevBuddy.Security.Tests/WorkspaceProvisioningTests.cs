using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Security.Tests;

/// <summary>
/// Closing the multi-workspace gap: only <c>IInstallationBootstrapper</c> could create a
/// workspace before this, and only once, ever. An existing workspace administrator can now
/// sponsor a new one through the ordinary pipeline and becomes its administrator — there is no
/// separate installation-administrator concept, and the check that gates this runs against the
/// sponsor workspace the same way every other permission check here does.
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class WorkspaceProvisioningTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task an_administrator_can_stand_up_a_new_workspace_with_a_first_project()
    {
        World sponsor = await _fixture.CreateWorldAsync();
        UserId administrator = await _fixture.CreateUserAsync($"sponsor-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(sponsor.Workspace, administrator, Role.Administrator);

        using Session session = _fixture.OpenSession(sponsor.Workspace);

        UseCaseResult<WorkspaceCreatedResponse> result = await session.RunAsync(
            new CreateWorkspaceUseCase(session.Resolve<IWorkspaceProvisioner>()),
            new CreateWorkspaceRequest(sponsor.Workspace, "New Tenant", "First Project"),
            World.Human(administrator));

        Assert.True(result.IsSuccess, $"Expected success but got {result.Outcome}: {result.Reason}");
        WorkspaceCreatedResponse created = result.Value!;

        Assert.NotEqual(sponsor.Workspace, created.WorkspaceId);
        Assert.NotNull(created.ProjectId);

        Assert.True(await session.Db.Workspaces.AnyAsync(row => row.Id == created.WorkspaceId.Value));
        Assert.True(await session.Db.Projects
            .IgnoreQueryFilters().AnyAsync(row => row.Id == created.ProjectId!.Value.Value));

        // The sponsoring administrator is the new workspace's administrator too, workspace-wide —
        // otherwise nobody could create the project that follows this one.
        var membership = await session.Db.Memberships.SingleAsync(row =>
            row.WorkspaceId == created.WorkspaceId.Value && row.UserId == administrator.Value);

        Assert.Null(membership.ProjectId);
        Assert.Equal((int)Role.Administrator, membership.Role);
    }

    [Fact]
    public async Task a_reviewer_cannot_create_a_workspace()
    {
        World sponsor = await _fixture.CreateWorldAsync();
        UserId reviewer = await _fixture.CreateUserAsync($"sponsor-reviewer-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(sponsor.Workspace, reviewer, Role.Reviewer);

        using Session session = _fixture.OpenSession(sponsor.Workspace);

        UseCaseResult<WorkspaceCreatedResponse> result = await session.RunAsync(
            new CreateWorkspaceUseCase(session.Resolve<IWorkspaceProvisioner>()),
            new CreateWorkspaceRequest(sponsor.Workspace, "Shadow Tenant"),
            World.Human(reviewer));

        Assert.Equal(ExecutionOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task a_project_administrator_cannot_create_a_workspace()
    {
        World sponsor = await _fixture.CreateWorldAsync();
        UserId projectAdmin = await _fixture.CreateUserAsync($"sponsor-proj-{Guid.NewGuid():N}@example.com");

        // Administrator of one project, not of the workspace. Standing up a new workspace is not
        // a project-scoped power.
        await _fixture.GrantAsync(sponsor.Workspace, projectAdmin, Role.Administrator, sponsor.AlphaId);

        using Session session = _fixture.OpenSession(sponsor.Workspace);

        UseCaseResult<WorkspaceCreatedResponse> result = await session.RunAsync(
            new CreateWorkspaceUseCase(session.Resolve<IWorkspaceProvisioner>()),
            new CreateWorkspaceRequest(sponsor.Workspace, "Another Tenant"),
            World.Human(projectAdmin));

        Assert.Equal(ExecutionOutcome.Denied, result.Outcome);
    }
}
