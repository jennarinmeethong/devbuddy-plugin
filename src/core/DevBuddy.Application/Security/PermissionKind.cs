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

    // Added in Phase 8, when the administration UI needed a way to set a workspace up. Until
    // then the only thing that could create anything was the one-time bootstrap, which meant a
    // second person could not be onboarded at all.

    /// <summary>Create and rename projects inside a workspace.</summary>
    ManageProjects = 13,

    /// <summary>
    /// Create and describe work items. Separate from ManageProjects and set lower, because
    /// registering the work a draft is about is part of contributing, not of administering.
    /// </summary>
    ManageWorkItems = 14,

    /// <summary>Create an account for somebody who does not have one yet.</summary>
    ManageAccounts = 15,

    /// <summary>
    /// Mint and revoke credentials for your own processes: the token a locally launched plugin
    /// puts in its configuration.
    /// <para>
    /// Held by every role, including Viewer, and that is not a loosening. A machine token carries
    /// exactly the permissions its owner already has, so being able to make one grants nothing
    /// new; refusing it would only mean a viewer could read knowledge in a browser and not from
    /// the editor they actually work in.
    /// </para>
    /// </summary>
    ManageOwnCredentials = 16,

    // Added closing v1 gaps after Phase 11.

    /// <summary>Create, rename, delete a team, and add or remove its members.</summary>
    ManageTeams = 17,

    /// <summary>
    /// Create a new, separate workspace, sponsored by one the caller already administers.
    /// <para>
    /// Not an installation-wide superuser flag — there is no such concept in this system, and
    /// adding one would be a materially different security model from everything else here. An
    /// existing workspace administrator can stand up another workspace, the same way the one-time
    /// bootstrap stands up the first one, and becomes its administrator in turn.
    /// </para>
    /// </summary>
    ProvisionWorkspace = 18,
}
