using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Work;

/// <summary>
/// A person with an interest in a work item. Held by name and role rather than by user
/// identifier, because the people who matter to a handover often have no account here.
/// </summary>
public sealed record Stakeholder
{
    public Stakeholder(string name, StakeholderRole role, string? contact = null)
    {
        Name = Guard.NotLongerThan(Guard.NotBlank(name, nameof(name)), 200, nameof(name));
        Role = Guard.Defined(role, nameof(role));
        Contact = contact is null
            ? null
            : Guard.NotLongerThan(Guard.NotBlank(contact, nameof(contact)), 320, nameof(contact));
    }

    public string Name { get; }

    public StakeholderRole Role { get; }

    public string? Contact { get; }
}
