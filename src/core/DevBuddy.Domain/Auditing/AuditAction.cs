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
    SourcesSynchronised = 10,
    AiAccessEnabled = 11,
    AiAccessDisabled = 12,
    MembershipGranted = 13,
    MembershipRevoked = 14,
    ExportCreated = 15,
    BackupCreated = 16,
    BackupRestored = 17,
    AccessDenied = 18,
}
