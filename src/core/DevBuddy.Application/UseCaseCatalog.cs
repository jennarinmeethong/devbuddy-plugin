using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Auditing;

namespace DevBuddy.Application;

/// <summary>
/// Every operation the system performs, in one place, with its permission, its AI exposure, and
/// what it audits.
/// <para>
/// This exists so that the answer to "what can AI reach" is a list you can read rather than a
/// property you have to derive by opening forty files. Phase 7 builds the MCP tool surface from
/// <see cref="AiExposed"/> and pins it with a test, which is control SB-07.
/// </para>
/// <para>
/// The eighteen AI-exposed entries are exactly the categories info.md permits: search, get,
/// analyse, create a draft, and generate a handover. Everything else is Denied, and is therefore
/// absent from the MCP tool list rather than merely refused by it.
/// </para>
/// </summary>
public static class UseCaseCatalog
{
    // Reading. AI may search and get, narrowed to the requesting user permissions.
    public static UseCaseDescriptor SearchKnowledge { get; } = new(
        "search_knowledge", PermissionKind.ReadKnowledge, AiExposure.Allowed,
        AuditAction.KnowledgeSearched, redactsOutput: true);

    public static UseCaseDescriptor GetRecord { get; } = new(
        "get_record", PermissionKind.ReadKnowledge, AiExposure.Allowed,
        AuditAction.RecordViewed, redactsOutput: true);

    public static UseCaseDescriptor GetWorkItem { get; } = new(
        "get_work_item", PermissionKind.ReadKnowledge, AiExposure.Allowed,
        AuditAction.RecordViewed, redactsOutput: true);

    public static UseCaseDescriptor ListProjects { get; } = new(
        "list_projects", PermissionKind.ReadKnowledge, AiExposure.Allowed,
        AuditAction.RecordViewed, redactsOutput: false);

    public static UseCaseDescriptor ViewRecordHistory { get; } = new(
        "view_record_history", PermissionKind.ReadKnowledge, AiExposure.Allowed,
        AuditAction.RecordViewed, redactsOutput: true);

    public static UseCaseDescriptor CompareSnapshots { get; } = new(
        "compare_snapshots", PermissionKind.ReadKnowledge, AiExposure.Allowed,
        AuditAction.RecordViewed, redactsOutput: true);

    // Analysis. Read-only, and never executes anything in the repository under study (SB-04).
    public static UseCaseDescriptor AnalyzeProject { get; } = Analysis("analyze_project");

    public static UseCaseDescriptor AnalyzeCode { get; } = Analysis("analyze_code");

    public static UseCaseDescriptor AnalyzeDocuments { get; } = Analysis("analyze_documents");

    public static UseCaseDescriptor AnalyzeArchitecture { get; } = Analysis("analyze_architecture");

    public static UseCaseDescriptor AnalyzeGitHistory { get; } = Analysis("analyze_git_history");

    public static UseCaseDescriptor AnalyzeWorkItems { get; } = Analysis("analyze_work_items");

    public static UseCaseDescriptor AnalyzeTestEvidence { get; } = Analysis("analyze_test_evidence");

    public static UseCaseDescriptor AnalyzeChangeImpact { get; } = Analysis("analyze_change_impact");

    // Handover.
    public static UseCaseDescriptor GenerateHandover { get; } = new(
        "generate_handover", PermissionKind.ReadKnowledge, AiExposure.Allowed,
        AuditAction.HandoverGenerated, redactsOutput: true);

    public static UseCaseDescriptor FindOpenQuestions { get; } = new(
        "find_open_questions", PermissionKind.ReadKnowledge, AiExposure.Allowed,
        AuditAction.RecordViewed, redactsOutput: true);

    public static UseCaseDescriptor FindMissingEvidence { get; } = new(
        "find_missing_evidence", PermissionKind.ReadKnowledge, AiExposure.Allowed,
        AuditAction.RecordViewed, redactsOutput: true);

    // Drafting. The one write operation AI may perform, and it produces a draft that no reader
    // of published knowledge sees until a human approves it.
    public static UseCaseDescriptor CreateDraft { get; } = new(
        "create_draft", PermissionKind.CreateDraft, AiExposure.Allowed,
        AuditAction.DraftCreated, redactsOutput: false);

    // Lifecycle. Human-gated, every one of them.
    public static UseCaseDescriptor ReviseDraft { get; } = new(
        "revise_draft", PermissionKind.CreateDraft, AiExposure.Denied,
        AuditAction.RevisionAdded, redactsOutput: false);

    public static UseCaseDescriptor SubmitForApproval { get; } = new(
        "submit_for_approval", PermissionKind.CreateDraft, AiExposure.Denied,
        AuditAction.ApprovalRequested, redactsOutput: false);

    public static UseCaseDescriptor ApproveRecord { get; } = new(
        "approve_record", PermissionKind.ReviewRecord, AiExposure.Denied,
        AuditAction.RecordApproved, redactsOutput: false);

    public static UseCaseDescriptor RequestCorrection { get; } = new(
        "request_correction", PermissionKind.ReviewRecord, AiExposure.Denied,
        AuditAction.CorrectionRequested, redactsOutput: false);

    public static UseCaseDescriptor PublishRecord { get; } = new(
        "publish_record", PermissionKind.PublishRecord, AiExposure.Denied,
        AuditAction.RecordPublished, redactsOutput: false);

    public static UseCaseDescriptor ArchiveRecord { get; } = new(
        "archive_record", PermissionKind.ArchiveRecord, AiExposure.Denied,
        AuditAction.RecordArchived, redactsOutput: false);

    // Sources and knowledge quality.
    public static UseCaseDescriptor SyncSources { get; } = new(
        "sync_sources", PermissionKind.ManageSources, AiExposure.Denied,
        AuditAction.SourcesSynchronized, redactsOutput: false);

    public static UseCaseDescriptor ValidateProvenance { get; } = QualitySweep("validate_provenance");

    public static UseCaseDescriptor DetectDuplicates { get; } = QualitySweep("detect_duplicates");

    public static UseCaseDescriptor DetectStaleness { get; } = QualitySweep("detect_staleness");

    public static UseCaseDescriptor Reindex { get; } = new(
        "reindex", PermissionKind.ManageIndex, AiExposure.Denied,
        AuditAction.IndexRebuilt, redactsOutput: false);

    // Safety. Denied to AI on purpose: the scanner is a control applied to AI output, not a
    // service offered to it.
    public static UseCaseDescriptor DetectSecrets { get; } = new(
        "detect_secrets", PermissionKind.ScanContent, AiExposure.Denied,
        AuditAction.ContentScanned, redactsOutput: false);

    public static UseCaseDescriptor RedactSensitiveData { get; } = new(
        "redact_sensitive_data", PermissionKind.ScanContent, AiExposure.Denied,
        AuditAction.ContentScanned, redactsOutput: false);

    // Administration.
    public static UseCaseDescriptor ExportProject { get; } = new(
        "export_project", PermissionKind.AdministerSystem, AiExposure.Denied,
        AuditAction.ExportCreated, redactsOutput: false);

    public static UseCaseDescriptor BackupSystem { get; } = new(
        "backup_system", PermissionKind.AdministerSystem, AiExposure.Denied,
        AuditAction.BackupCreated, redactsOutput: false);

    public static UseCaseDescriptor RestoreSystem { get; } = new(
        "restore_system", PermissionKind.AdministerSystem, AiExposure.Denied,
        AuditAction.BackupRestored, redactsOutput: false);

    public static UseCaseDescriptor CheckSystemHealth { get; } = new(
        "check_system_health", PermissionKind.AdministerSystem, AiExposure.Denied,
        AuditAction.HealthChecked, redactsOutput: false);

    // Access management.
    public static UseCaseDescriptor GrantMembership { get; } = new(
        "grant_membership", PermissionKind.ManageAccess, AiExposure.Denied,
        AuditAction.MembershipGranted, redactsOutput: false);

    public static UseCaseDescriptor RevokeMembership { get; } = new(
        "revoke_membership", PermissionKind.ManageAccess, AiExposure.Denied,
        AuditAction.MembershipRevoked, redactsOutput: false);

    public static UseCaseDescriptor EnableProjectAiAccess { get; } = new(
        "enable_project_ai_access", PermissionKind.ManageAccess, AiExposure.Denied,
        AuditAction.AiAccessEnabled, redactsOutput: false);

    public static UseCaseDescriptor DisableProjectAiAccess { get; } = new(
        "disable_project_ai_access", PermissionKind.ManageAccess, AiExposure.Denied,
        AuditAction.AiAccessDisabled, redactsOutput: false);

    public static UseCaseDescriptor ReadAuditHistory { get; } = new(
        "read_audit_history", PermissionKind.ReadAudit, AiExposure.Denied,
        AuditAction.AuditRead, redactsOutput: false);

    /// <summary>Every operation. Adding a use case without adding it here fails a test.</summary>
    public static IReadOnlyList<UseCaseDescriptor> All { get; } =
    [
        SearchKnowledge, GetRecord, GetWorkItem, ListProjects, ViewRecordHistory, CompareSnapshots,
        AnalyzeProject, AnalyzeCode, AnalyzeDocuments, AnalyzeArchitecture, AnalyzeGitHistory,
        AnalyzeWorkItems, AnalyzeTestEvidence, AnalyzeChangeImpact,
        GenerateHandover, FindOpenQuestions, FindMissingEvidence,
        CreateDraft,
        ReviseDraft, SubmitForApproval, ApproveRecord, RequestCorrection, PublishRecord, ArchiveRecord,
        SyncSources, ValidateProvenance, DetectDuplicates, DetectStaleness, Reindex,
        DetectSecrets, RedactSensitiveData,
        ExportProject, BackupSystem, RestoreSystem, CheckSystemHealth,
        GrantMembership, RevokeMembership, EnableProjectAiAccess, DisableProjectAiAccess,
        ReadAuditHistory,
    ];

    /// <summary>
    /// The operations that may be exposed as MCP tools. Phase 7 builds the tool list from this
    /// and asserts equality, so a use case cannot leak onto the AI surface by being added.
    /// </summary>
    public static IReadOnlyList<UseCaseDescriptor> AiExposed { get; } =
        [.. All.Where(descriptor => descriptor.AiExposure == AiExposure.Allowed)];

    private static UseCaseDescriptor Analysis(string name) => new(
        name, PermissionKind.AnalyzeProject, AiExposure.Allowed,
        AuditAction.AnalysisRun, redactsOutput: true);

    private static UseCaseDescriptor QualitySweep(string name) => new(
        name, PermissionKind.ManageIndex, AiExposure.Denied,
        AuditAction.QualitySweepRun, redactsOutput: false);
}
