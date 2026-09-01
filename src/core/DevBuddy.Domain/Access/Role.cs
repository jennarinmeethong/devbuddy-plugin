namespace DevBuddy.Domain.Access;

/// <summary>
/// The four roles required by info.md. Ordered by capability so a permission check can
/// compare rather than enumerate, but every check still happens server-side against a
/// specific resource: a role alone never authorises anything.
/// </summary>
public enum Role
{
    Viewer = 1,
    Contributor = 2,
    Reviewer = 3,
    Administrator = 4,
}
