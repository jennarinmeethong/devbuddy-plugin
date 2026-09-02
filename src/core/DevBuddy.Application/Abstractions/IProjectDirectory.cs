using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Reads the tenancy graph. Added in Phase 2: the plan port list did not name it, but listing
/// the projects a user can reach has to read projects from somewhere, and putting that on the
/// knowledge repository would have blurred two responsibilities.
/// <para>
/// The user identifier is a required argument, not an afterthought: this port never returns the
/// full project list, only the part the named user is a member of.
/// </para>
/// </summary>
public interface IProjectDirectory
{
    Task<IReadOnlyList<Project>> ListProjectsForUserAsync(
        WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken);

    Task<Project?> FindProjectAsync(ProjectScope scope, CancellationToken cancellationToken);

    Task AddProjectAsync(Project project, CancellationToken cancellationToken);
}
