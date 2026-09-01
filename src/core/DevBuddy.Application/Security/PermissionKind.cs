namespace DevBuddy.Application.Security;

/// <summary>
/// What a use case needs before it may run. Named PermissionKind rather than Permission
/// because that suffix is reserved for code-access permission types. Every use case names exactly one, in its descriptor,
/// so the question "what does this operation require" has a single answer that a test can read.
/// </summary>
public enum PermissionKind
{
    /// <summary>Read published knowledge, work items, and record history.</summary>
    ReadKnowledge = 1,

    /// <summary>Run read-only analysis over a project, its code, documents, and history.</summary>
    AnalyzeProject = 2,

    /// <summary>Create a draft record.</summary>
    CreateDraft = 3,

    /// <summary>Approve a draft or send it back for correction.</summary>
    ReviewRecord = 4,

    /// <summary>Publish an approved record.</summary>
    PublishRecord = 5,

    /// <summary>Archive a record.</summary>
    ArchiveRecord = 6,

    /// <summary>Import from and re-check against source systems.</summary>
    ManageSources = 7,

    /// <summary>Rebuild the search index and run knowledge-quality sweeps.</summary>
    ManageIndex = 8,

    /// <summary>Run secret detection and redaction over supplied material.</summary>
    ScanContent = 9,

    /// <summary>Grant and revoke membership, and change project AI access.</summary>
    ManageAccess = 10,

    /// <summary>Read the audit history.</summary>
    ReadAudit = 11,

    /// <summary>Export, back up, restore, and inspect system health.</summary>
    AdministerSystem = 12,
}
