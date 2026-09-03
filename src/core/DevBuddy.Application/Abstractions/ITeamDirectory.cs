using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Team administration: the entity and its table existed since Phase 1 with nothing reading or
/// writing them. Closing that gap, not extending it — a team is a grouping of users inside a
/// workspace, and carries no role or permission of its own. Permissions still come from
/// <see cref="Membership"/>, exactly as they did before this port existed.
/// </summary>
public interface ITeamDirectory
{
    Task<IReadOnlyList<Team>> ListTeamsAsync(WorkspaceId workspaceId, CancellationToken cancellationToken);

    Task<Team?> FindTeamAsync(TeamId id, WorkspaceId workspaceId, CancellationToken cancellationToken);

    Task AddTeamAsync(Team team, CancellationToken cancellationToken);

    Task UpdateTeamAsync(Team team, CancellationToken cancellationToken);

    Task DeleteTeamAsync(TeamId id, WorkspaceId workspaceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserId>> ListTeamMembersAsync(TeamId id, CancellationToken cancellationToken);

    Task AddTeamMemberAsync(TeamId id, WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken);

    Task RemoveTeamMemberAsync(TeamId id, UserId userId, CancellationToken cancellationToken);
}
