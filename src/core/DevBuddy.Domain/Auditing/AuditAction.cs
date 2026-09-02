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
}
