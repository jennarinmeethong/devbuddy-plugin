using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Tenancy;

/// <summary>
/// An environment a project is deployed to. Named DeploymentEnvironment to avoid colliding
/// with System.Environment.
/// </summary>
public sealed class DeploymentEnvironment
{
    public DeploymentEnvironment(
        DeploymentEnvironmentId id,
        ProjectScope scope,
        string name,
        EnvironmentKind kind)
    {
        Id = id;
        Scope = scope;
        Name = Guard.NotLongerThan(Guard.NotBlank(name, nameof(name)), 200, nameof(name));
        Kind = Guard.Defined(kind, nameof(kind));
    }

    public DeploymentEnvironmentId Id { get; }

    public ProjectScope Scope { get; }

    public string Name { get; }

    public EnvironmentKind Kind { get; }

    /// <summary>
    /// True when the AI data policy treats this environment as holding production data,
    /// which is denied by default and needs a separately approved bounded scope.
    /// </summary>
    public bool HoldsProductionData => Kind == EnvironmentKind.Production;
}
