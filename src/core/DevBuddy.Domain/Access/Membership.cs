using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Domain.Access;

/// <summary>
/// Grants one user a role, either across a whole workspace or on a single project.
/// A project grant never widens to the workspace.
/// </summary>
public sealed class Membership
{
    private Membership(
        MembershipId id,
        UserId userId,
        WorkspaceId workspaceId,
        ProjectId? projectId,
        Role role,
        DateTimeOffset grantedAt,
        UserId grantedBy)
    {
        if (workspaceId.Value == Guid.Empty)
        {
            throw new DomainValidationException("A membership requires a workspace identifier.");
        }

        Id = id;
        UserId = userId;
        WorkspaceId = workspaceId;
        ProjectId = projectId;
        Role = Guard.Defined(role, nameof(role));
        GrantedAt = Guard.Utc(grantedAt, nameof(grantedAt));
        GrantedBy = grantedBy;
    }

    public MembershipId Id { get; }

    public UserId UserId { get; }

    public WorkspaceId WorkspaceId { get; }

    /// <summary>Null for a workspace-wide grant.</summary>
    public ProjectId? ProjectId { get; }

    public Role Role { get; }

    public DateTimeOffset GrantedAt { get; }

    public UserId GrantedBy { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static Membership ForWorkspace(
        MembershipId id,
        UserId userId,
        WorkspaceId workspaceId,
        Role role,
        DateTimeOffset grantedAt,
        UserId grantedBy) =>
        new(id, userId, workspaceId, projectId: null, role, grantedAt, grantedBy);

    public static Membership ForProject(
        MembershipId id,
        UserId userId,
        ProjectScope scope,
        Role role,
        DateTimeOffset grantedAt,
        UserId grantedBy) =>
        new(id, userId, scope.WorkspaceId, scope.ProjectId, role, grantedAt, grantedBy);

    /// <summary>
    /// True when this grant applies to the given scope. A revoked grant matches nothing,
    /// which is what makes revocation take effect on the next request.
    /// </summary>
    public bool Covers(ProjectScope scope)
    {
        if (!IsActive || WorkspaceId != scope.WorkspaceId)
        {
            return false;
        }

        return ProjectId is null || ProjectId.Value == scope.ProjectId;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        if (RevokedAt is not null)
        {
            throw new InvalidTransitionException("This membership has already been revoked.");
        }

        RevokedAt = Guard.Utc(revokedAt, nameof(revokedAt));
    }
}
