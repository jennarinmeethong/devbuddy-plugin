using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Infrastructure.Security;

/// <summary>
/// The server-side answer to "may this caller do this, here".
/// <para>
/// Control SB-11 lives here. The scope in the request is a claim the caller made, and every step
/// below treats it that way: the user is loaded, their active grants are read from the database,
/// and the grant that covers the claimed scope decides the answer. Nothing is inferred from the
/// request itself.
/// </para>
/// <para>
/// Every path returns a denial unless a specific check passes. There is no branch that falls
/// through to allow.
/// </para>
/// </summary>
internal sealed class AuthorizationService(DevBuddyDbContext db) : IAuthorizationService
{
    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));

    public async Task<AuthorizationDecision> AuthorizeAsync(
        AuthorizationRequest request, CancellationToken cancellationToken)
    {
        Guard.NotNull(request, nameof(request));

        if (request.Caller.IsAnonymous)
        {
            return AuthorizationDecision.Deny("No identity was resolved.");
        }

        UserRow? user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == request.Caller.UserId.Value, cancellationToken);

        if (user is null)
        {
            return AuthorizationDecision.Deny("The caller does not exist.");
        }

        if (user.IsDisabled)
        {
            // Checked on every request, not only at sign-in, so disabling an account takes effect
            // while that person still holds a valid access token.
            return AuthorizationDecision.Deny("The account is disabled.");
        }

        Role? role = await EffectiveRoleAsync(request, cancellationToken);

        if (role is null)
        {
            // Deliberately the same message whether the caller has no grant, the grant was
            // revoked, or the project belongs to someone else entirely. A denial that explains
            // itself tells the caller what exists.
            return AuthorizationDecision.Deny("The caller has no access to this scope.");
        }

        if (!RolePermissions.Grants(role.Value, request.Permission))
        {
            return AuthorizationDecision.Deny(
                $"The role {role.Value} does not carry {request.Permission}.");
        }

        if (request.Caller.Channel == AccessChannel.Ai)
        {
            return await AuthorizeAiChannelAsync(request, cancellationToken);
        }

        return AuthorizationDecision.Allow();
    }

    /// <summary>
    /// The strongest role among the caller active grants that cover this scope, or null when none
    /// does. A revoked grant covers nothing, which is what makes revocation take effect on the
    /// next request rather than at the next sign-in.
    /// </summary>
    private async Task<Role?> EffectiveRoleAsync(
        AuthorizationRequest request, CancellationToken cancellationToken)
    {
        List<MembershipRow> rows = await _db.Memberships
            .AsNoTracking()
            .Where(membership => membership.WorkspaceId == request.WorkspaceId.Value
                && membership.UserId == request.Caller.UserId.Value
                && membership.RevokedAt == null)
            .ToListAsync(cancellationToken);

        Role? strongest = null;

        foreach (MembershipRow row in rows)
        {
            if (!Covers(row, request.ProjectId))
            {
                continue;
            }

            var role = (Role)row.Role;

            if (strongest is null || role > strongest)
            {
                strongest = role;
            }
        }

        return strongest;
    }

    /// <summary>
    /// A workspace-wide grant covers every project and every workspace-level operation. A project
    /// grant covers that project only, and never a workspace-level operation: being a contributor
    /// on one project is not a reason to see the workspace membership list.
    /// </summary>
    private static bool Covers(MembershipRow membership, ProjectId? requestedProject)
    {
        if (membership.ProjectId is null)
        {
            return true;
        }

        return requestedProject is { } project && membership.ProjectId.Value == project.Value;
    }

    /// <summary>
    /// The extra hurdle for the AI channel: the project owner has to have opened this specific
    /// project (SB-08). Permission alone is not enough, and neither is the AI credential.
    /// </summary>
    private async Task<AuthorizationDecision> AuthorizeAiChannelAsync(
        AuthorizationRequest request, CancellationToken cancellationToken)
    {
        if (request.ProjectId is not { } projectId)
        {
            // A workspace-level operation has no project policy to consult. The only one exposed
            // to AI is listing projects, and that use case filters the list to AI-enabled
            // projects itself.
            return AuthorizationDecision.Allow();
        }

        ProjectAiAccessPolicyRow? policy = await _db.AiAccessPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.WorkspaceId == request.WorkspaceId.Value
                    && candidate.ProjectId == projectId.Value,
                cancellationToken);

        // No row means never configured, which means denied. A project nobody has thought about
        // is not a project anyone opened to AI.
        return policy is { IsEnabled: true }
            ? AuthorizationDecision.Allow()
            : AuthorizationDecision.Deny("External AI access is not enabled for this project.");
    }
}
