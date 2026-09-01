using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Domain.Access;

/// <summary>
/// Whether external AI may read this project. Denied by default, and the default is
/// structural: a policy object constructed for a project starts disabled, so a project with
/// no policy row and a project with an untouched policy behave identically.
/// Enabling requires a named human and a timestamp, which is what makes SB-08 auditable.
/// </summary>
public sealed class ProjectAiAccessPolicy
{
    public ProjectAiAccessPolicy(ProjectScope scope)
    {
        Scope = scope;
        IsEnabled = false;
    }

    public ProjectScope Scope { get; }

    public bool IsEnabled { get; private set; }

    public UserId? EnabledBy { get; private set; }

    public DateTimeOffset? EnabledAt { get; private set; }

    /// <summary>
    /// The separately approved bounded scope that permits otherwise denied material, if one
    /// exists. Null means customer, production, and personal data stay denied.
    /// </summary>
    public string? BoundedDataScope { get; private set; }

    public void Enable(UserId enabledBy, DateTimeOffset enabledAt, string? boundedDataScope = null)
    {
        IsEnabled = true;
        EnabledBy = enabledBy;
        EnabledAt = Guard.Utc(enabledAt, nameof(enabledAt));
        BoundedDataScope = boundedDataScope is null
            ? null
            : Guard.NotLongerThan(Guard.NotBlank(boundedDataScope, nameof(boundedDataScope)), 2000, nameof(boundedDataScope));
    }

    public void Disable()
    {
        IsEnabled = false;
        EnabledBy = null;
        EnabledAt = null;
        BoundedDataScope = null;
    }
}
