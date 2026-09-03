using NpgsqlTypes;

namespace DevBuddy.Infrastructure.Persistence;

/// <summary>
/// The database schema, written out as plain classes.
/// <para>
/// These are deliberately separate from the domain aggregates rather than mapped onto them. Three
/// reasons, in order of importance: the aggregates keep their invariants with no persistence
/// concession (no parameterless constructor that leaves a record half-built); the schema a later
/// owner has to understand is written down rather than inferred from configuration; and the
/// ports already save whole aggregates explicitly, so change tracking would buy nothing.
/// </para>
/// <para>
/// The cost is the mapping code in <c>Persistence/Mapping</c>, which is real. See ADR-0011.
/// </para>
/// </summary>
internal interface ITenantScopedRow
{
    Guid WorkspaceId { get; }

    Guid ProjectId { get; }
}

internal sealed class WorkspaceRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class TeamRow
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Who is in a team. Grouping only — a team carries no role or permission of its own, so this
/// join is deliberately thinner than <see cref="MembershipRow"/>.
/// </summary>
internal sealed class TeamMemberRow
{
    public Guid TeamId { get; set; }

    /// <summary>
    /// Denormalised from the team, so the tenant query filter can apply to this row directly
    /// without a join.
    /// </summary>
    public Guid WorkspaceId { get; set; }

    public Guid UserId { get; set; }
}

internal sealed class ProjectRow
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class SourceRepositoryRow : ITenantScopedRow
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid ProjectId { get; set; }

    public int Provider { get; set; }

    public string RemoteLocator { get; set; } = string.Empty;

    public string DefaultBranch { get; set; } = string.Empty;
}

internal sealed class DeploymentEnvironmentRow : ITenantScopedRow
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Kind { get; set; }
}

internal sealed class UserRow
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Upper-cased for lookup. Stored rather than computed in the query so sign-in can use an
    /// index and an ordinal comparison, instead of asking the database to fold case on every row.
    /// </summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsDisabled { get; set; }
}

internal sealed class MembershipRow
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid WorkspaceId { get; set; }

    /// <summary>Null for a workspace-wide grant.</summary>
    public Guid? ProjectId { get; set; }

    public int Role { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    public Guid GrantedBy { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

internal sealed class ProjectAiAccessPolicyRow : ITenantScopedRow
{
    public Guid WorkspaceId { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary>
    /// Denied by default. A project with no row and a project with an untouched row behave the
    /// same way, which is what makes forgetting to configure a project safe (SB-08).
    /// </summary>
    public bool IsEnabled { get; set; }

    public Guid? EnabledBy { get; set; }

    public DateTimeOffset? EnabledAt { get; set; }

    public string? BoundedDataScope { get; set; }
}

internal sealed class WorkItemRow : ITenantScopedRow
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid ProjectId { get; set; }

    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The work item type. Stored as its integer value; 4 is change_request and 5 is code_review,
    /// and they are never collapsed into a shared abbreviation.
    /// </summary>
    public int Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Goal { get; set; } = string.Empty;

    public string? InScope { get; set; }

    public string? Exclusions { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public List<StakeholderJson> Stakeholders { get; set; } = [];

    public List<RelatedModuleJson> RelatedModules { get; set; } = [];
}

internal sealed class KnowledgeRecordRow : ITenantScopedRow
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid WorkItemId { get; set; }

    public int Kind { get; set; }

    public int Status { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastUpdatedAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Which revision readers of published knowledge see. Null until first published.</summary>
    public int? PublishedRevisionNumber { get; set; }

    public List<RecordRevisionRow> Revisions { get; set; } = [];

    public List<RecordApprovalRow> Approvals { get; set; } = [];

    public List<RecordCorrectionRow> Corrections { get; set; } = [];
}

internal sealed class RecordRevisionRow
{
    public Guid RecordId { get; set; }

    public int Number { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>The Markdown body, exactly as a reviewer read it.</summary>
    public string Body { get; set; } = string.Empty;

    public Dictionary<string, string> FrontMatter { get; set; } = [];

    public ProvenanceJson Provenance { get; set; } = new();

    /// <summary>
    /// Stored rather than recomputed on read. If the two ever disagree, something rewrote a
    /// revision in place, and a test can say so.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    /// <summary>
    /// Generated by PostgreSQL from the title and body. Full-text search only: no embeddings and
    /// no vector column in v1 (ADR-0003).
    /// </summary>
    public NpgsqlTsVector? SearchVector { get; set; }
}

internal sealed class RecordApprovalRow
{
    public Guid RecordId { get; set; }

    public int Sequence { get; set; }

    public Guid ApproverId { get; set; }

    /// <summary>The content the approver read. Publication compares against this (SB-23).</summary>
    public string ApprovedContentHash { get; set; } = string.Empty;

    public int ApprovedRevisionNumber { get; set; }

    public DateTimeOffset ApprovedAt { get; set; }

    public bool ApproverWasDraftCreator { get; set; }
}

internal sealed class RecordCorrectionRow
{
    public Guid RecordId { get; set; }

    public int Sequence { get; set; }

    public Guid RequestedBy { get; set; }

    public int TargetRevisionNumber { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; }
}

internal sealed class EvidenceObjectRow : ITenantScopedRow
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid ProjectId { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; }

    public Guid CapturedBy { get; set; }

    public int RedactionState { get; set; }

    public DateTimeOffset? ScannedAt { get; set; }
}

internal sealed class AuditEventRow
{
    public Guid Id { get; set; }

    /// <summary>Null for a system-wide action such as a backup.</summary>
    public Guid? WorkspaceId { get; set; }

    /// <summary>Null for a workspace-level action such as granting membership.</summary>
    public Guid? ProjectId { get; set; }

    public Guid ActorId { get; set; }

    public int Action { get; set; }

    public int Outcome { get; set; }

    /// <summary>An identifier for what was acted on. Never the content (SB-19).</summary>
    public string ResourceReference { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Metadata about the action: which revision an approval covered, whether the approver was
    /// also the author, what state a record ended in. The domain caps every value at 200
    /// characters, so this column cannot become a copy of the content (SB-19).
    /// </summary>
    public Dictionary<string, string> Details { get; set; } = [];
}

internal sealed class SourceSnapshotRow : ITenantScopedRow
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid RepositoryId { get; set; }

    public string Reference { get; set; } = string.Empty;

    public string CommitId { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; }

    public List<string> Links { get; set; } = [];
}

// Shapes stored as jsonb. Written as small explicit types rather than serialising the domain
// value objects directly, so what lands in the column is legible to someone reading the database
// with psql and no access to this code.

internal sealed class ProvenanceJson
{
    public int SourceKind { get; set; }

    public string SourceLocator { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }

    public List<EvidenceReferenceJson> Evidence { get; set; } = [];
}

internal sealed class EvidenceReferenceJson
{
    public Guid EvidenceObjectId { get; set; }

    public string Description { get; set; } = string.Empty;
}

internal sealed class StakeholderJson
{
    public string Name { get; set; } = string.Empty;

    public int Role { get; set; }

    public string? Contact { get; set; }
}

internal sealed class RelatedModuleJson
{
    public Guid RepositoryId { get; set; }

    public string? ModulePath { get; set; }
}

// Identity (Phase 4). Credentials are kept apart from the user record on purpose: reading a user
// is an everyday operation, and a password hash should not travel with it.

internal sealed class UserCredentialRow
{
    public Guid UserId { get; set; }

    /// <summary>Produced by the platform password hasher. Never a reversible encoding.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public int FailedAttempts { get; set; }

    /// <summary>Set while the account is locked, cleared on a successful sign-in.</summary>
    public DateTimeOffset? LockoutEndsAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A credential a process presents instead of signing in, stored only as a hash.
/// <para>
/// No rotation and no family: this one is meant to be presented repeatedly, which is exactly why
/// it does not live in the refresh-token table. What bounds it instead is an expiry it cannot
/// exceed, a revocation its owner can perform, and a last-used timestamp so a token nobody uses
/// any more is visible as one.
/// </para>
/// </summary>
internal sealed class MachineTokenRow
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    /// <summary>What its owner called it, so they can tell it from the others when revoking one.</summary>
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}

internal sealed class RefreshTokenRow
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>
    /// SHA-256 of the token, never the token. A database dump must not hand an attacker working
    /// credentials.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Rotation chain. Presenting a token that was already exchanged revokes the whole family,
    /// because the only ways that happens are theft and a broken client.
    /// </summary>
    public Guid FamilyId { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

internal sealed class RecoveryTokenRow
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}
