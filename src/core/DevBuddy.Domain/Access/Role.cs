namespace DevBuddy.Domain.Access;

/// <summary>
/// The four roles required by info.md, and one narrow role for a background job.
/// <para>
/// The numbers are storage, not rank. <see cref="IndexMaintainer"/> is numbered after
/// <see cref="Administrator"/> because a role is stored as its number and a new one can only be
/// appended, and it carries far less. So no check may compare roles: what a role may do is looked up
/// in the application's permission table, and every check still happens server-side against a
/// specific resource. A role alone never authorises anything.
/// </para>
/// </summary>
public enum Role
{
    Viewer = 1,
    Contributor = 2,
    Reviewer = 3,
    Administrator = 4,

    /// <summary>
    /// Reads knowledge and maintains the index, and nothing else. It exists so the stale-record
    /// sweep's token does not need an administrator's reach (info.md, 2026-09-21).
    /// </summary>
    IndexMaintainer = 5,
}
