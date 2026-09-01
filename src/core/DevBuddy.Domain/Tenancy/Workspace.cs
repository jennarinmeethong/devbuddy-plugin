using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Tenancy;

/// <summary>
/// The outermost tenant boundary. Nothing crosses a workspace without an explicit
/// authorization decision made outside the domain.
/// </summary>
public sealed class Workspace
{
    public Workspace(WorkspaceId id, string name, UserId createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        Name = Guard.NotLongerThan(Guard.NotBlank(name, nameof(name)), 200, nameof(name));
        CreatedBy = createdBy;
        CreatedAt = Guard.Utc(createdAt, nameof(createdAt));
    }

    public WorkspaceId Id { get; }

    public string Name { get; private set; }

    public UserId CreatedBy { get; }

    public DateTimeOffset CreatedAt { get; }

    public void Rename(string name) =>
        Name = Guard.NotLongerThan(Guard.NotBlank(name, nameof(name)), 200, nameof(name));
}
