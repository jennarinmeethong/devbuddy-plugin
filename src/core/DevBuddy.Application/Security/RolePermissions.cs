using DevBuddy.Domain.Access;

namespace DevBuddy.Application.Security;

/// <summary>
/// What each role may do. One table, in the application layer, so the question "who can publish"
/// has a single answer rather than one per endpoint.
/// <para>
/// Roles are cumulative by design: a reviewer can do everything a contributor can. That is the
/// shape info.md describes, and it keeps the table small enough to read in one go. It is not a
/// numeric comparison, though: <see cref="Grants"/> looks the permission up rather than comparing
/// role values, so introducing a role that is not a superset of the one below it stays possible
/// without rewriting every check.
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

        // Deny by default. A role added to the enum without being added here grants nothing,
        // which is the safe direction to fail.
        _ => [],
    };
}
