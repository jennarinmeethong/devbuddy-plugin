using System.Globalization;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Work;

namespace DevBuddy.Application.UseCases;

/// <summary>
/// Read models returned to callers.
/// <para>
/// Aggregates are never returned directly. A view is a deliberate decision about what leaves the
/// boundary, and it is the thing that declares which of its fields are free text and therefore
/// pass through the redactor. Handing out a <see cref="KnowledgeRecord"/> would give a caller
/// every revision, every approval, and every provenance locator whether or not the operation
/// called for them.
/// </para>
/// </summary>
/// <para>
/// The front matter and the evidence references travel with the body. The front matter is part of
/// the content hash an approval binds to, so a reviewer who could not see it approved something
/// they were never shown. A person revising a draft who could not read either would drop both
/// without knowing it, because a revision stores exactly what it is given.
/// </para>
public sealed record KnowledgeRecordView(
    KnowledgeRecordId RecordId,
    RecordKind Kind,
    RecordStatus Status,
    int RevisionNumber,
    int? PublishedRevisionNumber,
    string Title,
    string Body,
    ProvenanceView Provenance,
    DateTimeOffset LastUpdatedAt,
    IReadOnlyDictionary<string, string> FrontMatter,
    IReadOnlyList<EvidenceReferenceView> Evidence) : IRedactableResponse<KnowledgeRecordView>
{
    /// <summary>
    /// Every free-text field goes through the redactor, front matter values and evidence
    /// descriptions included. Front matter keys are field names the author chose and are left as
    /// they are, the same way the scanner reads a request's values and not its keys.
    /// </summary>
    public KnowledgeRecordView Redact(IRedactor redactor) =>
        this with
        {
            Title = redactor.Redact(Title),
            Body = redactor.Redact(Body),
            Provenance = Provenance.Redact(redactor),
            FrontMatter = FrontMatter.ToDictionary(
                entry => entry.Key, entry => redactor.Redact(entry.Value), StringComparer.Ordinal),
            Evidence = [.. Evidence.Select(reference => reference.Redact(redactor))],
        };

    /// <summary>Projects the revision a caller asked for. Identifiers are never redacted.</summary>
    public static KnowledgeRecordView From(KnowledgeRecord record, RecordRevision revision) =>
        new(
            record.Id,
            record.Kind,
            record.Status,
            revision.Number,
            record.PublishedRevisionNumber,
            revision.Title,
            revision.Body,
            ProvenanceView.From(revision.Provenance),
            record.LastUpdatedAt,
            revision.FrontMatter.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            [.. revision.Provenance.Evidence.Select(EvidenceReferenceView.From)]);
}

/// <summary>
/// One piece of evidence a revision cites: which stored artefact, and why it was attached. Never
/// the artefact's bytes, which only the human-only download route releases, and only once scanned.
/// </summary>
public sealed record EvidenceReferenceView(EvidenceObjectId EvidenceObjectId, string Description)
{
    public EvidenceReferenceView Redact(IRedactor redactor) =>
        this with { Description = redactor.Redact(Description) };

    public static EvidenceReferenceView From(EvidenceReference reference) =>
        new(reference.EvidenceObjectId, reference.Description);
}

/// <summary>
/// A reviewer sending a record back, and why. The reason is the reason the next revision exists,
/// so the person writing that revision has to be able to read it.
/// </summary>
public sealed record CorrectionView(
    UserId RequestedBy,
    int TargetRevisionNumber,
    string Reason,
    DateTimeOffset RequestedAt)
{
    public CorrectionView Redact(IRedactor redactor) =>
        this with { Reason = redactor.Redact(Reason) };

    public static CorrectionView From(CorrectionRequest correction) =>
        new(correction.RequestedBy, correction.TargetRevisionNumber, correction.Reason, correction.RequestedAt);
}

/// <summary>Where a revision came from, in the shape a reader needs.</summary>
public sealed record ProvenanceView(
    ProvenanceSourceKind SourceKind,
    string SourceLocator,
    string Author,
    DateTimeOffset RecordedAt,
    bool IsAiGenerated,
    int EvidenceCount,
    AiMarkingView? AiMarking = null)
{
    public ProvenanceView Redact(IRedactor redactor) =>
        this with
        {
            SourceLocator = redactor.Redact(SourceLocator),
            AiMarking = AiMarking is null ? null : AiMarking with { Reason = redactor.Redact(AiMarking.Reason) },
        };

    public static ProvenanceView From(Provenance provenance) =>
        new(
            provenance.SourceKind,
            provenance.SourceLocator,
            provenance.Author,
            provenance.RecordedAt,
            provenance.IsAiGenerated,
            provenance.Evidence.Count,
            provenance.AiMarking is { } marking
                ? new AiMarkingView(marking.MarkedBy, marking.MarkedAt, marking.Reason)
                : null);
}

/// <summary>
/// That an administrator, not the channel, recorded an AI as the writer: who, when, and why.
/// Null when the channel marked it, or when nobody did.
/// </summary>
public sealed record AiMarkingView(UserId MarkedBy, DateTimeOffset MarkedAt, string Reason);

/// <summary>Work identity, including the exclusions a later owner usually cannot find.</summary>
public sealed record WorkItemView(
    WorkItemId WorkItemId,
    string Key,
    WorkItemType Type,
    string Title,
    string Goal,
    string? InScope,
    string? Exclusions,
    IReadOnlyList<string> Stakeholders,
    int RecordCount) : IRedactableResponse<WorkItemView>
{
    public WorkItemView Redact(IRedactor redactor) =>
        this with
        {
            Title = redactor.Redact(Title),
            Goal = redactor.Redact(Goal),
            InScope = InScope is null ? null : redactor.Redact(InScope),
            Exclusions = Exclusions is null ? null : redactor.Redact(Exclusions),
        };

    public static WorkItemView From(WorkItem item, int recordCount) =>
        new(
            item.Id,
            item.Key,
            item.Type,
            item.Title,
            item.Goal,
            item.InScope,
            item.Exclusions,
            [.. item.Stakeholders.Select(stakeholder => $"{stakeholder.Name} ({stakeholder.Role})")],
            recordCount);
}

/// <summary>One project a caller is a member of.</summary>
/// <summary>
/// One project, and whether its owner has opened it to AI.
/// <para>
/// The policy travels with the project because the administration screen has to show it, and a
/// screen that had to ask separately for each project would make the most consequential switch in
/// the product the slowest thing on the page. On the AI channel the answer is always true, because
/// a project that is not enabled does not appear in the list at all.
/// </para>
/// </summary>
public sealed record ProjectSummary(
    ProjectId ProjectId, string Name, DateTimeOffset CreatedAt, bool AiAccessEnabled);

/// <summary>
/// One revision in a record history, with the approval that covers it if there is one. This is
/// what a reviewer reads before approving, so it carries the content hash: approval binds to
/// that value, and a reviewer who cannot see it cannot verify what they approved (SB-23).
/// </summary>
public sealed record RevisionSummary(
    int Number,
    string ContentHash,
    string Title,
    DateTimeOffset CreatedAt,
    ProvenanceView Provenance,
    bool IsPublished,
    ApprovalSummary? Approval) : IRedactableResponse<RevisionSummary>
{
    public RevisionSummary Redact(IRedactor redactor) =>
        this with { Title = redactor.Redact(Title), Provenance = Provenance.Redact(redactor) };
}

/// <summary>
/// An approval as the audit needs it: who, which exact revision, when, and whether the approver
/// was also the person who wrote the draft.
/// </summary>
public sealed record ApprovalSummary(
    UserId ApproverId,
    string ApprovedContentHash,
    int ApprovedRevisionNumber,
    DateTimeOffset ApprovedAt,
    bool ApproverWasDraftCreator);

/// <summary>
/// An acknowledgement that a lifecycle step happened, and what state it left behind.
/// <para>
/// It contributes that state to its own audit entry. Knowing that publish_record ran is much less
/// useful than knowing which revision went live.
/// </para>
/// </summary>
public record LifecycleResult(
    KnowledgeRecordId RecordId,
    RecordStatus Status,
    int CurrentRevisionNumber,
    int? PublishedRevisionNumber) : IAuditableResult
{
    public virtual IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["status"] = Status.ToString(),
            ["current_revision"] = CurrentRevisionNumber.ToString(CultureInfo.InvariantCulture),
            ["published_revision"] =
                PublishedRevisionNumber?.ToString(CultureInfo.InvariantCulture) ?? "none",
        };
}

/// <summary>
/// What an approval recorded.
/// <para>
/// info.md requires the audit history to record the approver, the exact approved revision, the
/// timestamp, and whether the approver was also the draft creator. All four are here, and all
/// four reach the audit store, because a record of "someone approved something" answers none of
/// the questions an investigation actually asks.
/// </para>
/// </summary>
public sealed record ApprovalResult(
    KnowledgeRecordId RecordId,
    RecordStatus Status,
    int CurrentRevisionNumber,
    int? PublishedRevisionNumber,
    UserId ApproverId,
    string ApprovedContentHash,
    int ApprovedRevisionNumber,
    DateTimeOffset ApprovedAt,
    bool ApproverWasDraftCreator)
    : LifecycleResult(RecordId, Status, CurrentRevisionNumber, PublishedRevisionNumber)
{
    public override IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["status"] = Status.ToString(),
            ["current_revision"] = CurrentRevisionNumber.ToString(CultureInfo.InvariantCulture),
            ["published_revision"] =
                PublishedRevisionNumber?.ToString(CultureInfo.InvariantCulture) ?? "none",
            ["approver"] = ApproverId.ToString(),
            ["approved_revision"] = ApprovedRevisionNumber.ToString(CultureInfo.InvariantCulture),

            // The hash, not the content. It is what the approval binds to, and it is what a later
            // reader needs in order to check that the published text is the text that was read.
            ["approved_content_hash"] = ApprovedContentHash,
            ["approved_at"] = ApprovedAt.ToString("O", CultureInfo.InvariantCulture),
            ["approver_was_draft_creator"] =
                ApproverWasDraftCreator ? "true" : "false",
        };
}
