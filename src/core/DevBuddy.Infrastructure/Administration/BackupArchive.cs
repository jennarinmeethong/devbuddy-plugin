using DevBuddy.Infrastructure.Persistence;

namespace DevBuddy.Infrastructure.Administration;

/// <summary>
/// Everything a backup holds, table by table.
/// <para>
/// A logical backup rather than a physical one, and that is a decision worth stating. A physical
/// backup means running <c>pg_dump</c>, and running anything from product code would put process
/// APIs in an assembly that a build-breaking test scans to keep them out (SB-04). Reading the rows
/// through the same context everything else uses costs a little performance and keeps that
/// guarantee intact.
/// </para>
/// <para>
/// What it means in practice: this is right for a self-hosted installation of the size v1 targets,
/// and it is not a replacement for a database-level backup at scale. An operator running something
/// large should take both, and `docs/operations/backup-and-restore.md` says so.
/// </para>
/// </summary>
internal sealed class BackupArchive
{
    /// <summary>
    /// Bumped when the shape of this file changes. A restore refuses a version it does not know
    /// rather than reading what it can and leaving the rest missing.
    /// </summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The last migration applied when this was taken. A restore into a database at a different
    /// migration is refused: the rows would not fit the columns.
    /// </summary>
    public string SchemaVersion { get; set; } = string.Empty;

    public List<WorkspaceRow> Workspaces { get; set; } = [];

    public List<TeamRow> Teams { get; set; } = [];

    public List<ProjectRow> Projects { get; set; } = [];

    public List<SourceRepositoryRow> SourceRepositories { get; set; } = [];

    public List<DeploymentEnvironmentRow> DeploymentEnvironments { get; set; } = [];

    public List<SourceSnapshotRow> SourceSnapshots { get; set; } = [];

    public List<UserRow> Users { get; set; } = [];

    public List<MembershipRow> Memberships { get; set; } = [];

    public List<ProjectAiAccessPolicyRow> AiAccessPolicies { get; set; } = [];

    /// <summary>
    /// Password hashes. Restored, because an installation whose people all had to recover their
    /// accounts after a restore would be a worse outcome than the one being recovered from.
    /// </summary>
    public List<UserCredentialRow> UserCredentials { get; set; } = [];

    /// <summary>
    /// Machine tokens. Restored, because they live in plugin configurations on other machines and
    /// a restore that silently invalidated all of them would look like the restore had failed.
    /// </summary>
    public List<MachineTokenRow> MachineTokens { get; set; } = [];

    public List<WorkItemRow> WorkItems { get; set; } = [];

    public List<KnowledgeRecordRow> KnowledgeRecords { get; set; } = [];

    public List<EvidenceObjectRow> EvidenceObjects { get; set; } = [];

    public List<AuditEventRow> AuditEvents { get; set; } = [];
}
