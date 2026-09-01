namespace DevBuddy.Domain.Evidence;

/// <summary>
/// What the secret scanner concluded about a piece of evidence.
/// <para>
/// NotScanned is the initial state and is deliberately not treated as safe. Control SB-17
/// requires scanning before retention and again before egress, so anything still NotScanned
/// must not be released.
/// </para>
/// </summary>
public enum RedactionState
{
    NotScanned = 1,
    Clean = 2,
    Redacted = 3,
    Blocked = 4,
}
