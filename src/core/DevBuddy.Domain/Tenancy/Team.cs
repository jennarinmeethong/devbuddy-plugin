using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Tenancy;

/// <summary>
/// A group of users inside one workspace. Membership is granted per team or per project;
/// a team never spans workspaces.
/// </summary>
public sealed class Team
{
    public Team(TeamId id, WorkspaceId workspaceId, string name)
    {
        if (workspaceId.Value == Guid.Empty)
        {
            throw new DomainValidationException("A team requires a workspace identifier.");
        }

        Id = id;
        WorkspaceId = workspaceId;
        Name = Guard.NotLongerThan(Guard.NotBlank(name, nameof(name)), 200, nameof(name));
    }

    public TeamId Id { get; }

    public WorkspaceId WorkspaceId { get; }

    public string Name { get; private set; }

    public void Rename(string name) =>
        Name = Guard.NotLongerThan(Guard.NotBlank(name, nameof(name)), 200, nameof(name));
}
