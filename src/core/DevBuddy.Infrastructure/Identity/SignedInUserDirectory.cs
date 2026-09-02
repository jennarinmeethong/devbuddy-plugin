using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Infrastructure.Identity;

/// <summary>
/// Answers "who am I and where do I belong", for the caller and nobody else.
/// <para>
/// The user identifier comes from the authenticated principal, never from the request, so there
/// is no parameter anybody could point at another account. What comes back is the caller's own
/// live grants: revoked ones are excluded, because a workspace somebody used to belong to is not
/// a workspace they can open.
/// </para>
/// </summary>
internal sealed class SignedInUserDirectory(DevBuddyDbContext db) : ISignedInUserDirectory
{
    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));

    public async Task<SignedInUser?> DescribeAsync(UserId userId, CancellationToken cancellationToken)
    {
        UserRow? user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId.Value, cancellationToken);

        if (user is null || user.IsDisabled)
        {
            // A disabled account is described as nothing rather than as an account with no
            // workspaces. The distinction matters: the second reads like a setup problem, and
            // somebody would try to fix it by granting membership.
            return null;
        }

        List<MembershipRow> grants = await _db.Memberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId.Value && membership.RevokedAt == null)
            .ToListAsync(cancellationToken);

        Guid[] workspaceIds = [.. grants.Select(grant => grant.WorkspaceId).Distinct()];

        // Read without the tenant filter because this query spans tenants by definition. The
        // workspaces table carries no filter for exactly this reason, and the result is still
        // bounded to the identifiers the caller holds a live grant for.
        Dictionary<Guid, string> names = await _db.Workspaces
            .AsNoTracking()
            .Where(workspace => workspaceIds.Contains(workspace.Id))
            .ToDictionaryAsync(workspace => workspace.Id, workspace => workspace.Name, cancellationToken);

        List<WorkspaceAccess> access = [];

        foreach (MembershipRow grant in grants.OrderBy(grant => grant.GrantedAt))
        {
            if (!names.TryGetValue(grant.WorkspaceId, out string? name))
            {
                continue;
            }

            var role = (Role)grant.Role;

            access.Add(new WorkspaceAccess(
                new WorkspaceId(grant.WorkspaceId),
                name,
                role,
                grant.ProjectId is { } projectId ? new ProjectId(projectId) : null,
                [.. RolePermissions.For(role).Select(permission => permission.ToString())]));
        }

        return new SignedInUser(userId, user.Email, user.DisplayName, access);
    }
}
