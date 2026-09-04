namespace DevBuddy.Domain.Auditing;

/// <summary>
/// What happened. Deliberately coarse: an audit entry says that an action occurred, never what
/// sensitive content it touched. That is control SB-19.
/// </summary>
public enum AuditAction
{
    KnowledgeSearched = 1,
    RecordViewed = 2,
    DraftCreated = 3,
    RevisionAdded = 4,
    CorrectionRequested = 5,
    RecordApproved = 6,
    RecordPublished = 7,
    RecordArchived = 8,
    EvidenceDownloaded = 9,
    SourcesSynchronized = 10,
    AiAccessEnabled = 11,
    AiAccessDisabled = 12,
    MembershipGranted = 13,
    MembershipRevoked = 14,
    ExportCreated = 15,
    BackupCreated = 16,
    BackupRestored = 17,
    AccessDenied = 18,

    // Added in Phase 2, when the use cases that perform these actions were written. Audit
    // actions are added with their use case rather than guessed in advance, so the enum stays
    // a description of what the system actually does.
    AnalysisRun = 19,
    HandoverGenerated = 20,
    ApprovalRequested = 21,
    QualitySweepRun = 22,
    IndexRebuilt = 23,
    ContentScanned = 24,
    HealthChecked = 25,
    AuditRead = 26,

    // Added in Phase 8 with the provisioning operations the administration UI needs.
    ProjectCreated = 27,
    WorkItemCreated = 28,
    AccountCreated = 29,

    // Phase 9, with the plugin packages that made the stdio transport real.
    MachineTokenIssued = 30,
    MachineTokenRevoked = 31,

    // Closing v1 gaps after Phase 11: project deletion, team administration, a second
    // workspace, and an actual export artefact.
    ProjectDeleted = 32,
    TeamCreated = 33,
    TeamRenamed = 34,
    TeamDeleted = 35,
    TeamMemberAdded = 36,
    TeamMemberRemoved = 37,
    WorkspaceCreated = 38,
    ExportDownloaded = 39,

    /// <summary>
    /// An artefact was attached to a project. Recorded because evidence is the material most
    /// likely to carry something nobody meant to store, so who put it there matters (SB-19).
    /// </summary>
    EvidenceCaptured = 40,

    /// <summary>What a project holds was listed. A read of metadata, not of bytes.</summary>
    EvidenceListed = 41,
}
