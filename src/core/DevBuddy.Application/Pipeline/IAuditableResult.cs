namespace DevBuddy.Application.Pipeline;

/// <summary>
/// A response that contributes metadata to its own audit entry.
/// <para>
/// The pipeline knows what operation ran and on which resource. It does not know which revision
/// an approval covered, or whether the approver was also the author, because only the use case
/// does. info.md requires the audit history to record exactly those things, so the use case has
/// to be able to say them.
/// </para>
/// <para>
/// What goes here is metadata: identifiers, hashes, numbers, states. Never titles, bodies, search
/// text, or anything else that would put the content an action touched into the audit store
/// (SB-19). The domain caps every value at 200 characters and refuses anything longer, so the
/// rule is enforced rather than trusted.
/// </para>
/// </summary>
public interface IAuditableResult
{
    IReadOnlyDictionary<string, string> AuditDetails { get; }
}
