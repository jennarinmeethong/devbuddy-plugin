namespace DevBuddy.Domain.Auditing;

/// <summary>
/// Whether the action succeeded. Denials are recorded as deliberately as successes: a refused
/// cross-project read is exactly the event an investigation needs to find.
/// </summary>
public enum AuditOutcome
{
    Succeeded = 1,
    Denied = 2,
    Failed = 3,
}
