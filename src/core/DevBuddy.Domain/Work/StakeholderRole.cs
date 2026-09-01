namespace DevBuddy.Domain.Work;

/// <summary>
/// Why a person is attached to a work item. Distinct from <see cref="Access.Role"/>, which
/// governs permissions: a stakeholder may have no account in this system at all.
/// </summary>
public enum StakeholderRole
{
    Requester = 1,
    Owner = 2,
    Reviewer = 3,
    Approver = 4,
    Informed = 5,
}
