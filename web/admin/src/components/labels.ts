import type { CreateDraftArguments, ListRecordsArguments } from "../api/operations";

/**
 * The words a person reads for the server's enum values, in one place so two screens cannot
 * disagree about what a status is called.
 */

export type RecordKind = CreateDraftArguments["kind"];

export type RecordStatus = NonNullable<ListRecordsArguments["statuses"]>[number];

export type SourceKind = CreateDraftArguments["provenance"]["sourceKind"];

export const KINDS: RecordKind[] = [
  "Decision",
  "TechnicalKnowledge",
  "ContextReference",
  "DeliveryState",
  "ChangeImpact",
  "Handover",
  "CodeReviewFeedback",
];

export const KIND_LABELS: Record<RecordKind, string> = {
  ContextReference: "Context reference",
  DeliveryState: "Delivery state",
  Decision: "Decision",
  TechnicalKnowledge: "Technical knowledge",
  ChangeImpact: "Change impact",
  Handover: "Handover",
  // Spelled out, as everywhere: code review and change request are different record types.
  CodeReviewFeedback: "Code review feedback",
};

export const STATUSES: RecordStatus[] = ["Draft", "PendingApproval", "Approved", "Published", "Archived"];

export const STATUS_LABELS: Record<RecordStatus, string> = {
  Draft: "Draft",
  PendingApproval: "Waiting for approval",
  Approved: "Approved",
  Published: "Published",
  Archived: "Archived",
};

/**
 * Where a draft came from. A person may say an assistant wrote what they are pasting, and the
 * server honours that; it never lets anyone say the opposite about something that arrived over
 * the AI channel.
 */
export const SOURCE_KINDS: SourceKind[] = [
  "HumanAuthored",
  "Document",
  "RepositoryAnalysis",
  "GitHistory",
  "IssueTracker",
  "PullRequest",
  "TestEvidence",
  "AiDraft",
];

export const SOURCE_KIND_LABELS: Record<SourceKind, string> = {
  HumanAuthored: "Written by a person",
  Document: "A document",
  RepositoryAnalysis: "Repository analysis",
  GitHistory: "Git history",
  IssueTracker: "Issue tracker",
  PullRequest: "Pull request",
  TestEvidence: "Test evidence",
  AiDraft: "Written by an AI assistant",
};
