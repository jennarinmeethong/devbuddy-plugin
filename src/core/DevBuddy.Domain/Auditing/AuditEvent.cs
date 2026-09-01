using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Domain.Auditing;

/// <summary>
/// One append-only entry. Immutable by construction: there is no method that changes an entry
/// after it exists, because an audit trail that can be edited is not an audit trail.
/// <para>
/// ResourceReference holds an identifier, never content. Control SB-19 forbids copying the
/// sensitive payload an action touched into the audit store.
/// </para>
/// </summary>
public sealed class AuditEvent
{
    public AuditEvent(
        AuditEventId id,
        ProjectScope scope,
        UserId actorId,
        AuditAction action,
        AuditOutcome outcome,
        string resourceReference,
        DateTimeOffset occurredAt)
    {
        Id = id;
        Scope = scope;
        ActorId = actorId;
        Action = Guard.Defined(action, nameof(action));
        Outcome = Guard.Defined(outcome, nameof(outcome));
        ResourceReference = Guard.NotLongerThan(
            Guard.NotBlank(resourceReference, nameof(resourceReference)), 500, nameof(resourceReference));
        OccurredAt = Guard.Utc(occurredAt, nameof(occurredAt));
    }

    public AuditEventId Id { get; }

    public ProjectScope Scope { get; }

    public UserId ActorId { get; }

    public AuditAction Action { get; }

    public AuditOutcome Outcome { get; }

    /// <summary>An identifier for what was acted on. Never the content itself.</summary>
    public string ResourceReference { get; }

    public DateTimeOffset OccurredAt { get; }
}
