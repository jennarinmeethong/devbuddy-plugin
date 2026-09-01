using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Domain.Common;
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
public sealed record KnowledgeRecordView(
    KnowledgeRecordId RecordId,
    RecordKind Kind,
    RecordStatus Status,
    int RevisionNumber,
    int? PublishedRevisionNumber,
    string Title,
    string Body,
    ProvenanceView Provenance,
    DateTimeOffset LastUpdatedAt) : IRedactableResponse<KnowledgeRecordView>
{
    public KnowledgeRecordView Redact(IRedactor redactor) =>
        this with
        {
            Title = redactor.Redact(Title),
            Body = redactor.Redact(Body),
            Provenance = Provenance.Redact(redactor),
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
            record.LastUpdatedAt);
}

/// <summary>Where a revision came from, in the shape a reader needs.</summary>
public sealed record ProvenanceView(
    ProvenanceSourceKind SourceKind,
    string SourceLocator,
    string Author,
    DateTimeOffset RecordedAt,
    bool IsAiGenerated,
    int EvidenceCount)
{
    public ProvenanceView Redact(IRedactor redactor) =>
        this with { SourceLocator = redactor.Redact(SourceLocator) };

    public static ProvenanceView From(Provenance provenance) =>
        new(
            provenance.SourceKind,
            provenance.SourceLocator,
            provenance.Author,
            provenance.RecordedAt,
            provenance.IsAiGenerated,
            provenance.Evidence.Count);
}

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
public sealed record ProjectSummary(ProjectId ProjectId, string Name, DateTimeOffset CreatedAt);

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

/// <summary>An acknowledgement that a lifecycle step happened, and what state it left behind.</summary>
public sealed record LifecycleResult(
    KnowledgeRecordId RecordId,
    RecordStatus Status,
    int CurrentRevisionNumber,
    int? PublishedRevisionNumber);
