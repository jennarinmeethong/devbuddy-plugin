import type { CreateDraftArguments, ListRecordsArguments } from "../api/operations";
import { m } from "../i18n";

/**
 * The words a person reads for the server's enum values, in one place so two screens cannot
 * disagree about what a status is called.
 *
 * Written in English and marked with `m`; a screen shows one through `t`, in the chosen language.
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
  ContextReference: m("Context reference"),
  DeliveryState: m("Delivery state"),
  Decision: m("Decision"),
  TechnicalKnowledge: m("Technical knowledge"),
  ChangeImpact: m("Change impact"),
  Handover: m("Handover"),
  // Spelled out, as everywhere: code review and change request are different record types.
  CodeReviewFeedback: m("Code review feedback"),
};

export const STATUSES: RecordStatus[] = ["Draft", "PendingApproval", "Approved", "Published", "Archived"];

export const STATUS_LABELS: Record<RecordStatus, string> = {
  Draft: m("Draft"),
  PendingApproval: m("Waiting for approval"),
  Approved: m("Approved"),
  Published: m("Published"),
  Archived: m("Archived"),
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
  HumanAuthored: m("Written by a person"),
  Document: m("A document"),
  RepositoryAnalysis: m("Repository analysis"),
  GitHistory: m("Git history"),
  IssueTracker: m("Issue tracker"),
  PullRequest: m("Pull request"),
  TestEvidence: m("Test evidence"),
  AiDraft: m("Written by an AI assistant"),
};

export type Role = "Viewer" | "Contributor" | "Reviewer" | "Administrator" | "IndexMaintainer";

export const ROLE_LABELS: Record<Role, string> = {
  Viewer: m("Viewer"),
  Contributor: m("Contributor"),
  Reviewer: m("Reviewer"),
  Administrator: m("Administrator"),
  IndexMaintainer: m("Index maintainer"),
};

/** A role as a person reads it. A role this client does not know is shown as the server named it. */
export function roleLabel(role: string): string {
  return role in ROLE_LABELS ? ROLE_LABELS[role as Role] : role;
}
