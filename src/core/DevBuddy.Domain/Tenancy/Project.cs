using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Tenancy;

/// <summary>
/// The unit that knowledge, permissions, and AI access are all scoped to.
/// </summary>
public sealed class Project
{
    public Project(ProjectId id, WorkspaceId workspaceId, string name, DateTimeOffset createdAt)
    {
        if (workspaceId.Value == Guid.Empty)
        {
            throw new DomainValidationException("A project requires a workspace identifier.");
        }

        Id = id;
        WorkspaceId = workspaceId;
        Name = Guard.NotLongerThan(Guard.NotBlank(name, nameof(name)), 200, nameof(name));
        CreatedAt = Guard.Utc(createdAt, nameof(createdAt));
    }

    public ProjectId Id { get; }

    public WorkspaceId WorkspaceId { get; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public ProjectScope Scope => new(WorkspaceId, Id);

    public void Rename(string name) =>
        Name = Guard.NotLongerThan(Guard.NotBlank(name, nameof(name)), 200, nameof(name));
}
