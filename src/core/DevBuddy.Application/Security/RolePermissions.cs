using DevBuddy.Domain.Access;

namespace DevBuddy.Application.Security;

/// <summary>
/// What each role may do. One table, in the application layer, so the question "who can publish"
/// has a single answer rather than one per endpoint.
/// <para>
/// The four roles info.md describes are cumulative: a reviewer can do everything a contributor
/// can. <see cref="Role.IndexMaintainer"/> is not part of that ladder. It carries reading, its own
/// credentials, and <see cref="PermissionKind.ManageIndex"/>, so a worker that sweeps for stale
/// records holds no administrator's reach. That is why <see cref="Grants"/> looks the permission up
/// rather than comparing role values, and why authorization asks whether <i>any</i> of a caller's
/// grants carries a permission rather than picking one "strongest" role.
/// </para>
/// <para>
/// A role never authorises anything on its own. The authorization service still has to establish
/// that the caller holds this role **in the scope being asked about** (SB-11).
/// </para>
/// </summary>
public static class RolePermissions
{
    private static readonly PermissionKind[] ViewerPermissions =
    [
        PermissionKind.ReadKnowledge,
        PermissionKind.ManageOwnCredentials,
    ];

    private static readonly PermissionKind[] ContributorPermissions =
    [
        .. ViewerPermissions,
        PermissionKind.AnalyzeProject,
        PermissionKind.CreateDraft,
        PermissionKind.ManageWorkItems,
    ];

    private static readonly PermissionKind[] ReviewerPermissions =
    [
        .. ContributorPermissions,
        PermissionKind.ReviewRecord,
        PermissionKind.PublishRecord,
        PermissionKind.ArchiveRecord,
    ];

    private static readonly PermissionKind[] AdministratorPermissions =
    [
        .. ReviewerPermissions,
        PermissionKind.ManageProjects,
        PermissionKind.ManageAccounts,
        PermissionKind.ManageSources,
        PermissionKind.ManageIndex,
        PermissionKind.ScanContent,
        PermissionKind.ManageAccess,
        PermissionKind.ReadAudit,
        PermissionKind.AdministerSystem,
        PermissionKind.ManageTeams,
        PermissionKind.ProvisionWorkspace,
    ];

    private static readonly PermissionKind[] IndexMaintainerPermissions =
    [
        PermissionKind.ReadKnowledge,
        PermissionKind.ManageOwnCredentials,
        PermissionKind.ManageIndex,
    ];

    /// <summary>Whether this role carries this permission. Unknown roles grant nothing.</summary>
    public static bool Grants(Role role, PermissionKind permission) =>
        For(role).Contains(permission);

    public static IReadOnlyList<PermissionKind> For(Role role) => role switch
    {
        Role.Viewer => ViewerPermissions,
        Role.Contributor => ContributorPermissions,
        Role.Reviewer => ReviewerPermissions,
        Role.Administrator => AdministratorPermissions,
        Role.IndexMaintainer => IndexMaintainerPermissions,

        // Deny by default. A role added to the enum without being added here grants nothing,
        // which is the safe direction to fail.
        _ => [],
    };
}
