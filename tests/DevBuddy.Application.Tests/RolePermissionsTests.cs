using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The role table. Small enough to assert exhaustively, and worth asserting exhaustively: it is
/// the difference between a viewer and someone who can publish.
/// </summary>
public sealed class RolePermissionsTests
{
    [Fact]
    public void a_viewer_can_only_read()
    {
        Assert.True(RolePermissions.Grants(Role.Viewer, PermissionKind.ReadKnowledge));

        Assert.False(RolePermissions.Grants(Role.Viewer, PermissionKind.CreateDraft));
        Assert.False(RolePermissions.Grants(Role.Viewer, PermissionKind.ReviewRecord));
        Assert.False(RolePermissions.Grants(Role.Viewer, PermissionKind.PublishRecord));
        Assert.False(RolePermissions.Grants(Role.Viewer, PermissionKind.ManageAccess));
        Assert.False(RolePermissions.Grants(Role.Viewer, PermissionKind.ReadAudit));
    }

    [Fact]
    public void a_contributor_can_draft_but_cannot_approve_their_own_work_into_publication()
    {
        Assert.True(RolePermissions.Grants(Role.Contributor, PermissionKind.CreateDraft));
        Assert.True(RolePermissions.Grants(Role.Contributor, PermissionKind.AnalyzeProject));

        // info.md permits a draft creator to approve their own draft, but only if they hold the
        // reviewer permission. Being the author is not itself a qualification.
        Assert.False(RolePermissions.Grants(Role.Contributor, PermissionKind.ReviewRecord));
        Assert.False(RolePermissions.Grants(Role.Contributor, PermissionKind.PublishRecord));
    }

    [Fact]
    public void a_reviewer_can_approve_and_publish_but_not_administer()
    {
        Assert.True(RolePermissions.Grants(Role.Reviewer, PermissionKind.ReviewRecord));
        Assert.True(RolePermissions.Grants(Role.Reviewer, PermissionKind.PublishRecord));
        Assert.True(RolePermissions.Grants(Role.Reviewer, PermissionKind.ArchiveRecord));

        Assert.False(RolePermissions.Grants(Role.Reviewer, PermissionKind.ManageAccess));
        Assert.False(RolePermissions.Grants(Role.Reviewer, PermissionKind.ReadAudit));
        Assert.False(RolePermissions.Grants(Role.Reviewer, PermissionKind.AdministerSystem));
    }

    [Fact]
    public void an_administrator_carries_every_permission()
    {
        foreach (PermissionKind permission in Enum.GetValues<PermissionKind>())
        {
            Assert.True(
                RolePermissions.Grants(Role.Administrator, permission),
                $"An administrator should carry {permission}.");
        }
    }

    [Fact]
    public void every_role_is_a_superset_of_the_one_below_it()
    {
        Role[] ascending = [Role.Viewer, Role.Contributor, Role.Reviewer, Role.Administrator];

        for (int index = 1; index < ascending.Length; index++)
        {
            IReadOnlyList<PermissionKind> lower = RolePermissions.For(ascending[index - 1]);
            IReadOnlyList<PermissionKind> higher = RolePermissions.For(ascending[index]);

            Assert.All(lower, permission => Assert.Contains(permission, higher));
        }
    }

    [Fact]
    public void an_index_maintainer_reads_and_maintains_the_index_and_nothing_else()
    {
        // Asserted as the exact set, not as a few spot checks, because the point of the role is
        // what it does not carry: a worker token holding it must not reach anything else.
        Assert.Equal(
            [PermissionKind.ReadKnowledge, PermissionKind.ManageIndex, PermissionKind.ManageOwnCredentials],
            RolePermissions.For(Role.IndexMaintainer).Order());
    }

    [Fact]
    public void an_index_maintainer_is_not_above_an_administrator_whatever_its_number()
    {
        // It is stored after Administrator and carries almost nothing. Nothing may read that
        // number as rank; IndexMaintainerRoleTests proves authorization does not.
        Assert.False(RolePermissions.Grants(Role.IndexMaintainer, PermissionKind.ManageAccess));
        Assert.False(RolePermissions.Grants(Role.IndexMaintainer, PermissionKind.CreateDraft));
        Assert.False(RolePermissions.Grants(Role.IndexMaintainer, PermissionKind.AdministerSystem));
    }

    [Fact]
    public void an_undefined_role_grants_nothing()
    {
        // A role added to the enum but not to the table grants nothing rather than everything.
        // That is the safe direction, and this test is what keeps it that way.
        Assert.Empty(RolePermissions.For((Role)99));

        foreach (PermissionKind permission in Enum.GetValues<PermissionKind>())
        {
            Assert.False(RolePermissions.Grants((Role)99, permission));
        }
    }

    [Fact]
    public void every_permission_a_use_case_declares_is_granted_by_some_role()
    {
        PermissionKind[] reachable =
        [
            .. Enum.GetValues<Role>()
                .SelectMany(RolePermissions.For)
                .Distinct()
        ];

        // A use case whose permission no role carries can never run. It would look implemented
        // and be unreachable, which is worse than not existing.
        foreach (Pipeline.UseCaseDescriptor descriptor in UseCaseCatalog.All)
        {
            Assert.Contains(descriptor.Permission, reachable);
        }
    }
}
