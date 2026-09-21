using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;

namespace DevBuddy.Security.Tests;

/// <summary>
/// The narrow role for the stale-record sweep (Phase 13, B8), through the real authorization
/// service over real PostgreSQL. The role is numbered after Administrator and carries far less,
/// which is only safe because authorization asks whether any covering grant carries a permission
/// rather than picking the highest-numbered role.
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class IndexMaintainerRoleTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task an_index_maintainer_can_maintain_the_index_and_read_and_nothing_else()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId worker = await _fixture.CreateUserAsync($"index-maintainer-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, worker, Role.IndexMaintainer);

        using Session session = _fixture.OpenSession(world.Workspace);
        IAuthorizationService authorization = session.Resolve<IAuthorizationService>();

        foreach (PermissionKind permission in Enum.GetValues<PermissionKind>())
        {
            AuthorizationDecision decision = await authorization.AuthorizeAsync(
                new AuthorizationRequest(World.Human(worker), permission, world.Workspace, world.AlphaId, "project"),
                TestToken.None);

            bool expected = permission is PermissionKind.ReadKnowledge
                or PermissionKind.ManageOwnCredentials
                or PermissionKind.ManageIndex;

            Assert.True(decision.IsAllowed == expected, $"{permission}: allowed {decision.IsAllowed}, expected {expected}.");
        }
    }

    [Fact]
    public async Task an_administrator_who_also_holds_index_maintainer_keeps_every_permission()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId person = await _fixture.CreateUserAsync($"admin-and-maintainer-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, person, Role.Administrator);
        await _fixture.GrantAsync(world.Workspace, person, Role.IndexMaintainer);

        using Session session = _fixture.OpenSession(world.Workspace);
        IAuthorizationService authorization = session.Resolve<IAuthorizationService>();

        // Picking the highest-numbered role would choose IndexMaintainer (5) over Administrator (4)
        // and refuse almost all of these.
        foreach (PermissionKind permission in Enum.GetValues<PermissionKind>())
        {
            foreach (ProjectId? project in new ProjectId?[] { null, world.AlphaId })
            {
                AuthorizationDecision decision = await authorization.AuthorizeAsync(
                    new AuthorizationRequest(World.Human(person), permission, world.Workspace, project, "resource"),
                    TestToken.None);

                Assert.True(decision.IsAllowed, $"{permission} on {(project is null ? "the workspace" : "a project")}: {decision.Reason}");
            }
        }
    }

    [Fact]
    public async Task a_project_scoped_index_maintainer_reaches_only_that_project()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId worker = await _fixture.CreateUserAsync($"scoped-maintainer-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, worker, Role.IndexMaintainer, world.AlphaId);

        using Session session = _fixture.OpenSession(world.Workspace);
        IAuthorizationService authorization = session.Resolve<IAuthorizationService>();

        Assert.True((await authorization.AuthorizeAsync(
            new AuthorizationRequest(World.Human(worker), PermissionKind.ManageIndex, world.Workspace, world.AlphaId, "project"),
            TestToken.None)).IsAllowed);
        Assert.False((await authorization.AuthorizeAsync(
            new AuthorizationRequest(World.Human(worker), PermissionKind.ManageIndex, world.Workspace, world.BetaId, "project"),
            TestToken.None)).IsAllowed);
    }
}
