using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Domain.Auditing;

/// <summary>
/// One append-only entry. Immutable by construction: there is no method that changes an entry
/// after it exists, because an audit trail that can be edited is not an audit trail.
/// <para>
/// Scope is a workspace and project pair rather than a <see cref="ProjectScope"/>, because real
/// auditable actions happen above a project: listing projects, granting membership, taking a
/// backup. Both are null for a system-wide operation.
/// </para>
/// <para>
/// ResourceReference holds an identifier, never content. Control SB-19 forbids copying the
/// sensitive payload an action touched into the audit store.
/// </para>
/// </summary>
public sealed class AuditEvent
{
    /// <summary>
    /// The longest reference an entry holds, the column's width. The pipeline refuses a request
    /// whose reference would not fit before anything is authorised or run.
    /// </summary>
    public const int MaxResourceReferenceLength = 500;

    public AuditEvent(
        AuditEventId id,
        WorkspaceId? workspaceId,
        ProjectId? projectId,
        UserId actorId,
        AuditChannel? channel,
        AuditAction action,
        AuditOutcome outcome,
        string resourceReference,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, string>? details = null)
    {
        if (projectId is not null && workspaceId is null)
        {
            throw new DomainValidationException("A project-scoped audit entry requires its workspace.");
        }

        Id = id;
        WorkspaceId = workspaceId;
        ProjectId = projectId;
        ActorId = actorId;
        Channel = channel is { } recorded ? Guard.Defined(recorded, nameof(channel)) : null;
        Action = Guard.Defined(action, nameof(action));
        Outcome = Guard.Defined(outcome, nameof(outcome));
        ResourceReference = Guard.NotLongerThan(
            Guard.NotBlank(resourceReference, nameof(resourceReference)), MaxResourceReferenceLength, nameof(resourceReference));
        OccurredAt = Guard.Utc(occurredAt, nameof(occurredAt));
        Details = Validate(details);
    }

    public AuditEventId Id { get; }

    public WorkspaceId? WorkspaceId { get; }

    public ProjectId? ProjectId { get; }

    public UserId ActorId { get; }

    /// <summary>
    /// The channel the request arrived on, or null for an entry written before the channel was
    /// recorded at all (before 2026-09-15).
    /// <para>
    /// Null is not a fourth channel and must never be read as <see cref="AuditChannel.Human"/>:
    /// those entries genuinely do not know, and a guess written back into them would be a record
    /// nobody could trust. The factories below take a channel that cannot be null, so every entry
    /// written from now on carries one; only rehydrating an old row reaches this constructor
    /// without one.
    /// </para>
    /// </summary>
    public AuditChannel? Channel { get; }

    public AuditAction Action { get; }

    public AuditOutcome Outcome { get; }

    /// <summary>An identifier for what was acted on. Never the content itself.</summary>
    public string ResourceReference { get; }

    public DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// Metadata about what happened: which revision was approved, whether the approver was also
    /// the author, what state the record ended in.
    /// <para>
    /// Metadata, not payload. Values are capped at 200 characters and the cap is the point:
    /// control SB-19 forbids copying the sensitive content an action touched into the audit
    /// store, and a field with no limit is where that would happen first. Record titles, bodies,
    /// and search text never go here; hashes, numbers, and states do.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, string> Details { get; }

    public static AuditEvent ForProject(
        AuditEventId id,
        ProjectScope scope,
        UserId actorId,
        AuditChannel channel,
        AuditAction action,
        AuditOutcome outcome,
        string resourceReference,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, string>? details = null) =>
        new(id, scope.WorkspaceId, scope.ProjectId, actorId, channel, action, outcome, resourceReference, occurredAt, details);

    public static AuditEvent ForWorkspace(
        AuditEventId id,
        WorkspaceId workspaceId,
        UserId actorId,
        AuditChannel channel,
        AuditAction action,
        AuditOutcome outcome,
        string resourceReference,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, string>? details = null) =>
        new(id, workspaceId, projectId: null, actorId, channel, action, outcome, resourceReference, occurredAt, details);

    /// <summary>For operations above any workspace, such as a backup or a restore.</summary>
    public static AuditEvent ForSystem(
        AuditEventId id,
        UserId actorId,
        AuditChannel channel,
        AuditAction action,
        AuditOutcome outcome,
        string resourceReference,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, string>? details = null) =>
        new(id, workspaceId: null, projectId: null, actorId, channel, action, outcome, resourceReference, occurredAt, details);

    /// <summary>
    /// Caps the shape of the detail bag. Refusing an oversized value is better than truncating
    /// one: a truncated secret is still a leak, and a refusal is visible.
    /// </summary>
    private static Dictionary<string, string> Validate(
        IReadOnlyDictionary<string, string>? details)
    {
        if (details is null || details.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        if (details.Count > 20)
        {
            throw new DomainValidationException(
                "An audit entry carries at most 20 detail values. It records what happened, not the thing it happened to.");
        }

        var copy = new Dictionary<string, string>(details.Count, StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> entry in details)
        {
            Guard.NotLongerThan(Guard.NotBlank(entry.Key, "detail key"), 64, "detail key");
            Guard.NotLongerThan(entry.Value ?? string.Empty, 200, $"detail value for {entry.Key}");
            copy[entry.Key] = entry.Value ?? string.Empty;
        }

        return copy;
    }
}
