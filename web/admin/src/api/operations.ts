// Generated from GET /operations. Do not edit by hand.
//
// Regenerate with:
//   DEVBUDDY_WRITE_CLIENT=1 dotnet test tests/DevBuddy.Api.Tests -c Release \
//     --filter FullyQualifiedName~GeneratedClientTests
//
// The test that writes this file also compares it. A server whose operations or
// argument shapes changed without the client being regenerated fails that test.

/** Every operation this deployment can perform. */
export type OperationName =
  | "search_knowledge"
  | "get_record"
  | "get_work_item"
  | "list_projects"
  | "view_record_history"
  | "compare_snapshots"
  | "analyze_project"
  | "analyze_code"
  | "analyze_documents"
  | "analyze_architecture"
  | "analyze_git_history"
  | "analyze_work_items"
  | "analyze_test_evidence"
  | "analyze_change_impact"
  | "generate_handover"
  | "find_open_questions"
  | "find_missing_evidence"
  | "create_draft"
  | "revise_draft"
  | "submit_for_approval"
  | "approve_record"
  | "request_correction"
  | "publish_record"
  | "archive_record"
  | "sync_sources"
  | "validate_provenance"
  | "detect_duplicates"
  | "detect_staleness"
  | "reindex"
  | "detect_secrets"
  | "redact_sensitive_data"
  | "export_project"
  | "backup_system"
  | "restore_system"
  | "check_system_health"
  | "grant_membership"
  | "revoke_membership"
  | "enable_project_ai_access"
  | "disable_project_ai_access"
  | "read_audit_history"
  | "list_memberships"
  | "create_project"
  | "create_work_item"
  | "create_user_account"
  | "list_work_items"
  | "list_records"
  ;

/** Permission names, as /me reports the ones a caller holds. */
export type PermissionName =
  | "AdministerSystem"
  | "AnalyzeProject"
  | "ArchiveRecord"
  | "CreateDraft"
  | "ManageAccess"
  | "ManageAccounts"
  | "ManageIndex"
  | "ManageProjects"
  | "ManageSources"
  | "ManageWorkItems"
  | "PublishRecord"
  | "ReadAudit"
  | "ReadKnowledge"
  | "ReviewRecord"
  | "ScanContent"
  ;

export type SearchKnowledgeArguments = {
  queryText: string;
  kinds?: Array<"ContextReference" | "DeliveryState" | "Decision" | "TechnicalKnowledge" | "ChangeImpact" | "Handover" | "CodeReviewFeedback"> | null;
  statuses?: Array<"Draft" | "PendingApproval" | "Approved" | "Published" | "Archived"> | null;
  maxResults?: number;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type SearchKnowledgeResult = {
  hits: Array<{
      recordId: string;
      kind: "ContextReference" | "DeliveryState" | "Decision" | "TechnicalKnowledge" | "ChangeImpact" | "Handover" | "CodeReviewFeedback";
      status: "Draft" | "PendingApproval" | "Approved" | "Published" | "Archived";
      title: string;
      snippet: string;
      rank: number;
    }>;
};

export type GetRecordArguments = {
  recordId: string;
  revisionNumber?: number | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type GetRecordResult = {
  recordId: string;
  kind: "ContextReference" | "DeliveryState" | "Decision" | "TechnicalKnowledge" | "ChangeImpact" | "Handover" | "CodeReviewFeedback";
  status: "Draft" | "PendingApproval" | "Approved" | "Published" | "Archived";
  revisionNumber: number;
  publishedRevisionNumber: number | null;
  title: string;
  body: string;
  provenance: {
    sourceKind: "HumanAuthored" | "RepositoryAnalysis" | "GitHistory" | "IssueTracker" | "PullRequest" | "Document" | "TestEvidence" | "AiDraft";
    sourceLocator: string;
    author: string;
    recordedAt: string;
    isAiGenerated: boolean;
    evidenceCount: number;
  };
  lastUpdatedAt: string;
};

export type GetWorkItemArguments = {
  workItemId: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type GetWorkItemResult = {
  workItemId: string;
  key: string;
  type: "Develop" | "Enhance" | "FixBug" | "ChangeRequest" | "CodeReview";
  title: string;
  goal: string;
  inScope: string | null;
  exclusions: string | null;
  stakeholders: Array<string>;
  recordCount: number;
};

export type ListProjectsArguments = {
  workspaceId: string;
};

export type ListProjectsResult = {
  projects: Array<{
      projectId: string;
      name: string;
      createdAt: string;
      aiAccessEnabled: boolean;
    }>;
};

export type ViewRecordHistoryArguments = {
  recordId: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type ViewRecordHistoryResult = {
  recordId: string;
  status: "Draft" | "PendingApproval" | "Approved" | "Published" | "Archived";
  revisions: Array<{
      number: number;
      contentHash: string;
      title: string;
      createdAt: string;
      provenance: {
        sourceKind: "HumanAuthored" | "RepositoryAnalysis" | "GitHistory" | "IssueTracker" | "PullRequest" | "Document" | "TestEvidence" | "AiDraft";
        sourceLocator: string;
        author: string;
        recordedAt: string;
        isAiGenerated: boolean;
        evidenceCount: number;
      };
      isPublished: boolean;
      approval: {
        approverId: string;
        approvedContentHash: string;
        approvedRevisionNumber: number;
        approvedAt: string;
        approverWasDraftCreator: boolean;
      } | null;
    }>;
};

export type CompareSnapshotsArguments = {
  repositoryId: string;
  earlierReference: string;
  laterReference: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type CompareSnapshotsResult = {
  differences: Array<{
      subject: string;
      before: string;
      after: string;
    }>;
};

export type AnalyzeProjectArguments = {
  repositoryId?: string | null;
  target?: string | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type AnalyzeProjectResult = {
  report: {
    kind: "Project" | "Code" | "Documents" | "Architecture" | "GitHistory" | "WorkItems" | "TestEvidence";
    summary: string;
    observations: Array<{
        subject: string;
        detail: string;
        sourceLocator: string;
      }>;
  };
};

export type AnalyzeCodeArguments = {
  repositoryId?: string | null;
  target?: string | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type AnalyzeCodeResult = {
  report: {
    kind: "Project" | "Code" | "Documents" | "Architecture" | "GitHistory" | "WorkItems" | "TestEvidence";
    summary: string;
    observations: Array<{
        subject: string;
        detail: string;
        sourceLocator: string;
      }>;
  };
};

export type AnalyzeDocumentsArguments = {
  repositoryId?: string | null;
  target?: string | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type AnalyzeDocumentsResult = {
  report: {
    kind: "Project" | "Code" | "Documents" | "Architecture" | "GitHistory" | "WorkItems" | "TestEvidence";
    summary: string;
    observations: Array<{
        subject: string;
        detail: string;
        sourceLocator: string;
      }>;
  };
};

export type AnalyzeArchitectureArguments = {
  repositoryId?: string | null;
  target?: string | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type AnalyzeArchitectureResult = {
  report: {
    kind: "Project" | "Code" | "Documents" | "Architecture" | "GitHistory" | "WorkItems" | "TestEvidence";
    summary: string;
    observations: Array<{
        subject: string;
        detail: string;
        sourceLocator: string;
      }>;
  };
};

export type AnalyzeGitHistoryArguments = {
  repositoryId?: string | null;
  target?: string | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type AnalyzeGitHistoryResult = {
  report: {
    kind: "Project" | "Code" | "Documents" | "Architecture" | "GitHistory" | "WorkItems" | "TestEvidence";
    summary: string;
    observations: Array<{
        subject: string;
        detail: string;
        sourceLocator: string;
      }>;
  };
};

export type AnalyzeWorkItemsArguments = {
  repositoryId?: string | null;
  target?: string | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type AnalyzeWorkItemsResult = {
  report: {
    kind: "Project" | "Code" | "Documents" | "Architecture" | "GitHistory" | "WorkItems" | "TestEvidence";
    summary: string;
    observations: Array<{
        subject: string;
        detail: string;
        sourceLocator: string;
      }>;
  };
};

export type AnalyzeTestEvidenceArguments = {
  repositoryId?: string | null;
  target?: string | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type AnalyzeTestEvidenceResult = {
  report: {
    kind: "Project" | "Code" | "Documents" | "Architecture" | "GitHistory" | "WorkItems" | "TestEvidence";
    summary: string;
    observations: Array<{
        subject: string;
        detail: string;
        sourceLocator: string;
      }>;
  };
};

export type AnalyzeChangeImpactArguments = {
  repositoryId: string;
  commitOrRange: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type AnalyzeChangeImpactResult = {
  commitOrRange: string;
  changedPaths: Array<string>;
  impact: Array<{
      subject: string;
      detail: string;
      sourceLocator: string;
    }>;
};

export type GenerateHandoverArguments = {
  workItemId: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type GenerateHandoverResult = {
  workItemId: string;
  title: string;
  sections: Array<{
      kind: "ContextReference" | "DeliveryState" | "Decision" | "TechnicalKnowledge" | "ChangeImpact" | "Handover" | "CodeReviewFeedback";
      content: string;
      recordCount: number;
    }>;
  openQuestions: Array<string>;
  missingEvidence: Array<string>;
  generatedAt: string;
};

export type FindOpenQuestionsArguments = {
  workItemId: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type FindOpenQuestionsResult = {
  questions: Array<string>;
};

export type FindMissingEvidenceArguments = {
  workItemId: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type FindMissingEvidenceResult = {
  gaps: Array<string>;
};

export type CreateDraftArguments = {
  workItemId: string;
  kind: "ContextReference" | "DeliveryState" | "Decision" | "TechnicalKnowledge" | "ChangeImpact" | "Handover" | "CodeReviewFeedback";
  title: string;
  body: string;
  provenance: {
    sourceKind: "HumanAuthored" | "RepositoryAnalysis" | "GitHistory" | "IssueTracker" | "PullRequest" | "Document" | "TestEvidence" | "AiDraft";
    sourceLocator: string;
    author: string;
    recordedAt: string;
    evidence?: Array<{
        evidenceObjectId: string;
        description: string;
      }> | null;
    isAiGenerated?: boolean;
  };
  frontMatter?: Record<string, string> | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type CreateDraftResult = {
  recordId: string;
  status: "Draft" | "PendingApproval" | "Approved" | "Published" | "Archived";
  currentRevisionNumber: number;
  publishedRevisionNumber: number | null;
};

export type ReviseDraftArguments = {
  recordId: string;
  title: string;
  body: string;
  provenance: {
    sourceKind: "HumanAuthored" | "RepositoryAnalysis" | "GitHistory" | "IssueTracker" | "PullRequest" | "Document" | "TestEvidence" | "AiDraft";
    sourceLocator: string;
    author: string;
    recordedAt: string;
    evidence?: Array<{
        evidenceObjectId: string;
        description: string;
      }> | null;
    isAiGenerated?: boolean;
  };
  frontMatter?: Record<string, string> | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type ReviseDraftResult = {
  recordId: string;
  status: "Draft" | "PendingApproval" | "Approved" | "Published" | "Archived";
  currentRevisionNumber: number;
  publishedRevisionNumber: number | null;
};

export type SubmitForApprovalArguments = {
  recordId: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type SubmitForApprovalResult = {
  recordId: string;
  status: "Draft" | "PendingApproval" | "Approved" | "Published" | "Archived";
  currentRevisionNumber: number;
  publishedRevisionNumber: number | null;
};

export type ApproveRecordArguments = {
  recordId: string;
  approvedContentHash: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type ApproveRecordResult = {
  approverId: string;
  approvedContentHash: string;
  approvedRevisionNumber: number;
  approvedAt: string;
  approverWasDraftCreator: boolean;
  recordId: string;
  status: "Draft" | "PendingApproval" | "Approved" | "Published" | "Archived";
  currentRevisionNumber: number;
  publishedRevisionNumber: number | null;
};

export type RequestCorrectionArguments = {
  recordId: string;
  reason: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type RequestCorrectionResult = {
  recordId: string;
  status: "Draft" | "PendingApproval" | "Approved" | "Published" | "Archived";
  currentRevisionNumber: number;
  publishedRevisionNumber: number | null;
};

export type PublishRecordArguments = {
  recordId: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type PublishRecordResult = {
  recordId: string;
  status: "Draft" | "PendingApproval" | "Approved" | "Published" | "Archived";
  currentRevisionNumber: number;
  publishedRevisionNumber: number | null;
};

export type ArchiveRecordArguments = {
  recordId: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type ArchiveRecordResult = {
  recordId: string;
  status: "Draft" | "PendingApproval" | "Approved" | "Published" | "Archived";
  currentRevisionNumber: number;
  publishedRevisionNumber: number | null;
};

export type SyncSourcesArguments = {
  repositoryId: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type SyncSourcesResult = {
  repositoryId: string;
  commitId: string;
  capturedAt: string;
  linkCount: number;
};

export type ValidateProvenanceArguments = {
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type ValidateProvenanceResult = {
  sweep: string;
  findings: Array<{
      recordId: string;
      rule: string;
      detail: string;
    }>;
};

export type DetectDuplicatesArguments = {
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type DetectDuplicatesResult = {
  sweep: string;
  findings: Array<{
      recordId: string;
      rule: string;
      detail: string;
    }>;
};

export type DetectStalenessArguments = {
  staleAfter: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type DetectStalenessResult = {
  sweep: string;
  findings: Array<{
      recordId: string;
      rule: string;
      detail: string;
    }>;
};

export type ReindexArguments = {
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type ReindexResult = {
  documentsIndexed: number;
};

export type DetectSecretsArguments = {
  content: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type DetectSecretsResult = {
  hasFindings: boolean;
  findings: Array<{
      ruleName: string;
      lineNumber: number;
      length: number;
    }>;
};

export type RedactSensitiveDataArguments = {
  content: string;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type RedactSensitiveDataResult = {
  redactedContent: string;
  findingCount: number;
};

export type ExportProjectArguments = {
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type ExportProjectResult = {
  reference: string;
  recordCount: number;
  evidenceCount: number;
  createdAt: string;
  expiresAt: string;
};

export type BackupSystemArguments = {
  workspaceId: string;
};

export type BackupSystemResult = {
  reference: string;
  sizeBytes: number;
  createdAt: string;
};

export type RestoreSystemArguments = {
  backupReference: string;
  workspaceId: string;
};

export type RestoreSystemResult = {
  reference: string;
  succeeded: boolean;
  detail: string;
};

export type CheckSystemHealthArguments = {
  workspaceId: string;
};

export type CheckSystemHealthResult = {
  isHealthy: boolean;
  components: Array<{
      name: string;
      isHealthy: boolean;
      detail: string;
    }>;
};

export type GrantMembershipArguments = {
  subjectUserId: string;
  role: "Viewer" | "Contributor" | "Reviewer" | "Administrator";
  scopedToProject?: string | null;
  workspaceId: string;
};

export type GrantMembershipResult = {
  membershipId: string;
  role: "Viewer" | "Contributor" | "Reviewer" | "Administrator";
  isActive: boolean;
};

export type RevokeMembershipArguments = {
  membershipId: string;
  workspaceId: string;
};

export type RevokeMembershipResult = {
  membershipId: string;
  role: "Viewer" | "Contributor" | "Reviewer" | "Administrator";
  isActive: boolean;
};

export type EnableProjectAiAccessArguments = {
  boundedDataScope?: string | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type EnableProjectAiAccessResult = {
  isEnabled: boolean;
  enabledBy: string | null;
  enabledAt: string | null;
};

export type DisableProjectAiAccessArguments = {
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type DisableProjectAiAccessResult = {
  isEnabled: boolean;
  enabledBy: string | null;
  enabledAt: string | null;
};

export type ReadAuditHistoryArguments = {
  occurredFrom: string;
  occurredUntil: string;
  actorId?: string | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type ReadAuditHistoryResult = {
  entries: Array<{
      id: string;
      workspaceId: string | null;
      projectId: string | null;
      actorId: string;
      action: "KnowledgeSearched" | "RecordViewed" | "DraftCreated" | "RevisionAdded" | "CorrectionRequested" | "RecordApproved" | "RecordPublished" | "RecordArchived" | "EvidenceDownloaded" | "SourcesSynchronized" | "AiAccessEnabled" | "AiAccessDisabled" | "MembershipGranted" | "MembershipRevoked" | "ExportCreated" | "BackupCreated" | "BackupRestored" | "AccessDenied" | "AnalysisRun" | "HandoverGenerated" | "ApprovalRequested" | "QualitySweepRun" | "IndexRebuilt" | "ContentScanned" | "HealthChecked" | "AuditRead" | "ProjectCreated" | "WorkItemCreated" | "AccountCreated";
      outcome: "Succeeded" | "Denied" | "Failed";
      resourceReference: string;
      occurredAt: string;
      details?: Record<string, string> | null;
    }>;
};

export type ListMembershipsArguments = {
  workspaceId: string;
};

export type ListMembershipsResult = {
  memberships: Array<{
      membershipId: string;
      userId: string;
      role: "Viewer" | "Contributor" | "Reviewer" | "Administrator";
      scopedToProject: string | null;
      isActive: boolean;
      grantedAt: string;
    }>;
};

export type CreateProjectArguments = {
  name: string;
  workspaceId: string;
};

export type CreateProjectResult = {
  projectId: string;
  name: string;
};

export type CreateWorkItemArguments = {
  key: string;
  type: "Develop" | "Enhance" | "FixBug" | "ChangeRequest" | "CodeReview";
  title: string;
  goal: string;
  inScope?: string | null;
  exclusions?: string | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type CreateWorkItemResult = {
  workItemId: string;
  key: string;
};

export type CreateUserAccountArguments = {
  email: string;
  displayName: string;
  role: "Viewer" | "Contributor" | "Reviewer" | "Administrator";
  scopedToProject?: string | null;
  workspaceId: string;
};

export type CreateUserAccountResult = {
  userId: string;
  membershipId: string;
  setupToken: string;
  setupTokenExpiresAt: string;
};

export type ListWorkItemsArguments = {
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type ListWorkItemsResult = {
  workItems: Array<{
      workItemId: string;
      key: string;
      type: "Develop" | "Enhance" | "FixBug" | "ChangeRequest" | "CodeReview";
      title: string;
      createdAt: string;
    }>;
};

export type ListRecordsArguments = {
  statuses?: Array<"Draft" | "PendingApproval" | "Approved" | "Published" | "Archived"> | null;
  scope: {
    workspaceId: string;
    projectId: string;
  };
};

export type ListRecordsResult = {
  records: Array<{
      recordId: string;
      workItemId: string;
      kind: "ContextReference" | "DeliveryState" | "Decision" | "TechnicalKnowledge" | "ChangeImpact" | "Handover" | "CodeReviewFeedback";
      status: "Draft" | "PendingApproval" | "Approved" | "Published" | "Archived";
      title: string;
      currentRevisionNumber: number;
      publishedRevisionNumber: number | null;
      updatedAt: string;
    }>;
};

/** Argument and result types, keyed by operation name. */
export interface Operations {
  "search_knowledge": { arguments: SearchKnowledgeArguments; result: SearchKnowledgeResult };
  "get_record": { arguments: GetRecordArguments; result: GetRecordResult };
  "get_work_item": { arguments: GetWorkItemArguments; result: GetWorkItemResult };
  "list_projects": { arguments: ListProjectsArguments; result: ListProjectsResult };
  "view_record_history": { arguments: ViewRecordHistoryArguments; result: ViewRecordHistoryResult };
  "compare_snapshots": { arguments: CompareSnapshotsArguments; result: CompareSnapshotsResult };
  "analyze_project": { arguments: AnalyzeProjectArguments; result: AnalyzeProjectResult };
  "analyze_code": { arguments: AnalyzeCodeArguments; result: AnalyzeCodeResult };
  "analyze_documents": { arguments: AnalyzeDocumentsArguments; result: AnalyzeDocumentsResult };
  "analyze_architecture": { arguments: AnalyzeArchitectureArguments; result: AnalyzeArchitectureResult };
  "analyze_git_history": { arguments: AnalyzeGitHistoryArguments; result: AnalyzeGitHistoryResult };
  "analyze_work_items": { arguments: AnalyzeWorkItemsArguments; result: AnalyzeWorkItemsResult };
  "analyze_test_evidence": { arguments: AnalyzeTestEvidenceArguments; result: AnalyzeTestEvidenceResult };
  "analyze_change_impact": { arguments: AnalyzeChangeImpactArguments; result: AnalyzeChangeImpactResult };
  "generate_handover": { arguments: GenerateHandoverArguments; result: GenerateHandoverResult };
  "find_open_questions": { arguments: FindOpenQuestionsArguments; result: FindOpenQuestionsResult };
  "find_missing_evidence": { arguments: FindMissingEvidenceArguments; result: FindMissingEvidenceResult };
  "create_draft": { arguments: CreateDraftArguments; result: CreateDraftResult };
  "revise_draft": { arguments: ReviseDraftArguments; result: ReviseDraftResult };
  "submit_for_approval": { arguments: SubmitForApprovalArguments; result: SubmitForApprovalResult };
  "approve_record": { arguments: ApproveRecordArguments; result: ApproveRecordResult };
  "request_correction": { arguments: RequestCorrectionArguments; result: RequestCorrectionResult };
  "publish_record": { arguments: PublishRecordArguments; result: PublishRecordResult };
  "archive_record": { arguments: ArchiveRecordArguments; result: ArchiveRecordResult };
  "sync_sources": { arguments: SyncSourcesArguments; result: SyncSourcesResult };
  "validate_provenance": { arguments: ValidateProvenanceArguments; result: ValidateProvenanceResult };
  "detect_duplicates": { arguments: DetectDuplicatesArguments; result: DetectDuplicatesResult };
  "detect_staleness": { arguments: DetectStalenessArguments; result: DetectStalenessResult };
  "reindex": { arguments: ReindexArguments; result: ReindexResult };
  "detect_secrets": { arguments: DetectSecretsArguments; result: DetectSecretsResult };
  "redact_sensitive_data": { arguments: RedactSensitiveDataArguments; result: RedactSensitiveDataResult };
  "export_project": { arguments: ExportProjectArguments; result: ExportProjectResult };
  "backup_system": { arguments: BackupSystemArguments; result: BackupSystemResult };
  "restore_system": { arguments: RestoreSystemArguments; result: RestoreSystemResult };
  "check_system_health": { arguments: CheckSystemHealthArguments; result: CheckSystemHealthResult };
  "grant_membership": { arguments: GrantMembershipArguments; result: GrantMembershipResult };
  "revoke_membership": { arguments: RevokeMembershipArguments; result: RevokeMembershipResult };
  "enable_project_ai_access": { arguments: EnableProjectAiAccessArguments; result: EnableProjectAiAccessResult };
  "disable_project_ai_access": { arguments: DisableProjectAiAccessArguments; result: DisableProjectAiAccessResult };
  "read_audit_history": { arguments: ReadAuditHistoryArguments; result: ReadAuditHistoryResult };
  "list_memberships": { arguments: ListMembershipsArguments; result: ListMembershipsResult };
  "create_project": { arguments: CreateProjectArguments; result: CreateProjectResult };
  "create_work_item": { arguments: CreateWorkItemArguments; result: CreateWorkItemResult };
  "create_user_account": { arguments: CreateUserAccountArguments; result: CreateUserAccountResult };
  "list_work_items": { arguments: ListWorkItemsArguments; result: ListWorkItemsResult };
  "list_records": { arguments: ListRecordsArguments; result: ListRecordsResult };
}

/** What each operation needs, and whether the AI surface may reach it. */
export const OPERATIONS: Record<OperationName, { permission: PermissionName; availableToAi: boolean }> = {
  "search_knowledge": { permission: "ReadKnowledge", availableToAi: true },
  "get_record": { permission: "ReadKnowledge", availableToAi: true },
  "get_work_item": { permission: "ReadKnowledge", availableToAi: true },
  "list_projects": { permission: "ReadKnowledge", availableToAi: true },
  "view_record_history": { permission: "ReadKnowledge", availableToAi: true },
  "compare_snapshots": { permission: "ReadKnowledge", availableToAi: true },
  "analyze_project": { permission: "AnalyzeProject", availableToAi: true },
  "analyze_code": { permission: "AnalyzeProject", availableToAi: true },
  "analyze_documents": { permission: "AnalyzeProject", availableToAi: true },
  "analyze_architecture": { permission: "AnalyzeProject", availableToAi: true },
  "analyze_git_history": { permission: "AnalyzeProject", availableToAi: true },
  "analyze_work_items": { permission: "AnalyzeProject", availableToAi: true },
  "analyze_test_evidence": { permission: "AnalyzeProject", availableToAi: true },
  "analyze_change_impact": { permission: "AnalyzeProject", availableToAi: true },
  "generate_handover": { permission: "ReadKnowledge", availableToAi: true },
  "find_open_questions": { permission: "ReadKnowledge", availableToAi: true },
  "find_missing_evidence": { permission: "ReadKnowledge", availableToAi: true },
  "create_draft": { permission: "CreateDraft", availableToAi: true },
  "revise_draft": { permission: "CreateDraft", availableToAi: false },
  "submit_for_approval": { permission: "CreateDraft", availableToAi: false },
  "approve_record": { permission: "ReviewRecord", availableToAi: false },
  "request_correction": { permission: "ReviewRecord", availableToAi: false },
  "publish_record": { permission: "PublishRecord", availableToAi: false },
  "archive_record": { permission: "ArchiveRecord", availableToAi: false },
  "sync_sources": { permission: "ManageSources", availableToAi: false },
  "validate_provenance": { permission: "ManageIndex", availableToAi: false },
  "detect_duplicates": { permission: "ManageIndex", availableToAi: false },
  "detect_staleness": { permission: "ManageIndex", availableToAi: false },
  "reindex": { permission: "ManageIndex", availableToAi: false },
  "detect_secrets": { permission: "ScanContent", availableToAi: false },
  "redact_sensitive_data": { permission: "ScanContent", availableToAi: false },
  "export_project": { permission: "AdministerSystem", availableToAi: false },
  "backup_system": { permission: "AdministerSystem", availableToAi: false },
  "restore_system": { permission: "AdministerSystem", availableToAi: false },
  "check_system_health": { permission: "AdministerSystem", availableToAi: false },
  "grant_membership": { permission: "ManageAccess", availableToAi: false },
  "revoke_membership": { permission: "ManageAccess", availableToAi: false },
  "enable_project_ai_access": { permission: "ManageAccess", availableToAi: false },
  "disable_project_ai_access": { permission: "ManageAccess", availableToAi: false },
  "read_audit_history": { permission: "ReadAudit", availableToAi: false },
  "list_memberships": { permission: "ManageAccess", availableToAi: false },
  "create_project": { permission: "ManageProjects", availableToAi: false },
  "create_work_item": { permission: "ManageWorkItems", availableToAi: false },
  "create_user_account": { permission: "ManageAccounts", availableToAi: false },
  "list_work_items": { permission: "ReadKnowledge", availableToAi: false },
  "list_records": { permission: "ReadKnowledge", availableToAi: false },
};
