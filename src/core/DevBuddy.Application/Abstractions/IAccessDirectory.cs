using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Membership and per-project AI access. Added in Phase 2 alongside
/// <see cref="IProjectDirectory"/>; granting access and changing an AI policy are human-gated
/// operations and never reach the MCP surface.
/// </summary>
public interface IAccessDirectory
{
    Task<IReadOnlyList<Membership>> ListMembershipsAsync(
        WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken);

    /// <summary>
    /// Every membership in the workspace, whoever holds it. Distinct from the per-user overload
    /// above, which answers a question about one person; this one answers a question about the
    /// workspace and is therefore an administrative read.
    /// </summary>
    Task<IReadOnlyList<Membership>> ListMembershipsForWorkspaceAsync(
        WorkspaceId workspaceId, CancellationToken cancellationToken);

    /// <summary>
    /// Every live grant this person holds, in every workspace. It spans tenants on purpose, and
    /// only one question needs it: whether an administrator's reach covers everywhere a password
    /// works. A password belongs to the installation, not to a workspace.
    /// </summary>
    Task<IReadOnlyList<Membership>> ListLiveMembershipsEverywhereAsync(
        UserId userId, CancellationToken cancellationToken);

    Task AddMembershipAsync(Membership membership, CancellationToken cancellationToken);

    Task UpdateMembershipAsync(Membership membership, CancellationToken cancellationToken);

    Task<Membership?> FindMembershipAsync(MembershipId id, CancellationToken cancellationToken);

    Task<ProjectAiAccessPolicy> GetAiAccessPolicyAsync(
        ProjectScope scope, CancellationToken cancellationToken);

    Task SaveAiAccessPolicyAsync(
        ProjectAiAccessPolicy policy, CancellationToken cancellationToken);
}
