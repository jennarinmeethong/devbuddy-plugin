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
    public AuditEvent(
        AuditEventId id,
        WorkspaceId? workspaceId,
        ProjectId? projectId,
        UserId actorId,
        AuditAction action,
        AuditOutcome outcome,
        string resourceReference,
        DateTimeOffset occurredAt)
    {
        if (projectId is not null && workspaceId is null)
        {
            throw new DomainValidationException("A project-scoped audit entry requires its workspace.");
        }

        Id = id;
        WorkspaceId = workspaceId;
        ProjectId = projectId;
        ActorId = actorId;
        Action = Guard.Defined(action, nameof(action));
        Outcome = Guard.Defined(outcome, nameof(outcome));
        ResourceReference = Guard.NotLongerThan(
            Guard.NotBlank(resourceReference, nameof(resourceReference)), 500, nameof(resourceReference));
        OccurredAt = Guard.Utc(occurredAt, nameof(occurredAt));
    }

    public AuditEventId Id { get; }

    public WorkspaceId? WorkspaceId { get; }

    public ProjectId? ProjectId { get; }

    public UserId ActorId { get; }

    public AuditAction Action { get; }

    public AuditOutcome Outcome { get; }

    /// <summary>An identifier for what was acted on. Never the content itself.</summary>
    public string ResourceReference { get; }

    public DateTimeOffset OccurredAt { get; }

    public static AuditEvent ForProject(
        AuditEventId id,
        ProjectScope scope,
        UserId actorId,
        AuditAction action,
        AuditOutcome outcome,
        string resourceReference,
        DateTimeOffset occurredAt) =>
        new(id, scope.WorkspaceId, scope.ProjectId, actorId, action, outcome, resourceReference, occurredAt);

    public static AuditEvent ForWorkspace(
        AuditEventId id,
        WorkspaceId workspaceId,
        UserId actorId,
        AuditAction action,
        AuditOutcome outcome,
        string resourceReference,
        DateTimeOffset occurredAt) =>
        new(id, workspaceId, projectId: null, actorId, action, outcome, resourceReference, occurredAt);

    /// <summary>For operations above any workspace, such as a backup or a restore.</summary>
    public static AuditEvent ForSystem(
        AuditEventId id,
        UserId actorId,
        AuditAction action,
        AuditOutcome outcome,
        string resourceReference,
        DateTimeOffset occurredAt) =>
        new(id, workspaceId: null, projectId: null, actorId, action, outcome, resourceReference, occurredAt);
}
