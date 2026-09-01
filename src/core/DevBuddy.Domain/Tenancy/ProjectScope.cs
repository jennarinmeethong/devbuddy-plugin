using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Tenancy;

/// <summary>
/// The workspace and project a record belongs to. Every scoped entity carries one, so the
/// requirement that every record and query is scoped becomes a type rather than a convention
/// that each new query has to remember.
/// </summary>
public readonly record struct ProjectScope
{
    public ProjectScope(WorkspaceId workspaceId, ProjectId projectId)
    {
        if (workspaceId.Value == Guid.Empty)
        {
            throw new DomainValidationException("A project scope requires a workspace identifier.");
        }

        if (projectId.Value == Guid.Empty)
        {
            throw new DomainValidationException("A project scope requires a project identifier.");
        }

        WorkspaceId = workspaceId;
        ProjectId = projectId;
    }

    public WorkspaceId WorkspaceId { get; }

    public ProjectId ProjectId { get; }

    public bool Contains(ProjectScope other) =>
        WorkspaceId == other.WorkspaceId && ProjectId == other.ProjectId;

    public override string ToString() => $"{WorkspaceId}/{ProjectId}";
}
