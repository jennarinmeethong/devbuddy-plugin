using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;

namespace DevBuddy.Domain.Knowledge;

/// <summary>
/// The aggregate that holds one piece of knowledge through its whole controlled lifecycle:
/// draft, correction, approval, publication, archive.
/// <para>
/// Three rules are enforced here rather than in a handler, because a handler can be bypassed by
/// a new caller and this cannot:
/// </para>
/// <list type="number">
/// <item>Publication requires an approval whose content hash equals the current revision (SB-23).</item>
/// <item>Revisions are appended, never rewritten (SB-24).</item>
/// <item>Every record is scoped to a workspace and project, and carries provenance (SB-25).</item>
/// </list>
/// <para>
/// Publishing a later revision does not erase the previously published one:
/// <see cref="PublishedRevisionNumber"/> keeps pointing at what is live while a new draft is
/// prepared, which is what keeps drafts genuinely separate from published knowledge (SB-26).
/// </para>
/// </summary>
public sealed class KnowledgeRecord
{
    private readonly List<RecordRevision> _revisions = [];
    private readonly List<Approval> _approvals = [];
    private readonly List<CorrectionRequest> _correctionRequests = [];

    private KnowledgeRecord(
        KnowledgeRecordId id,
        ProjectScope scope,
        WorkItemId workItemId,
        RecordKind kind,
        RecordRevision initialRevision,
        UserId createdBy)
    {
        if (workItemId.Value == Guid.Empty)
        {
            throw new DomainValidationException("A knowledge record must belong to a work item.");
        }

        Id = id;
        Scope = scope;
        WorkItemId = workItemId;
        Kind = Guard.Defined(kind, nameof(kind));
        CreatedBy = createdBy;
        CreatedAt = initialRevision.CreatedAt;
        LastUpdatedAt = initialRevision.CreatedAt;
        Status = RecordStatus.Draft;
        _revisions.Add(initialRevision);
    }

    public KnowledgeRecordId Id { get; }

    public ProjectScope Scope { get; }

    public WorkItemId WorkItemId { get; }

    public RecordKind Kind { get; }

    public RecordStatus Status { get; private set; }

    public UserId CreatedBy { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset LastUpdatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>Which revision is live. Null until the record has been published once.</summary>
    public int? PublishedRevisionNumber { get; private set; }

    public IReadOnlyList<RecordRevision> Revisions => _revisions;

    public IReadOnlyList<Approval> Approvals => _approvals;

    public IReadOnlyList<CorrectionRequest> CorrectionRequests => _correctionRequests;

    /// <summary>The newest revision: the one under edit or under review.</summary>
    public RecordRevision CurrentRevision => _revisions[^1];

    /// <summary>The revision visible to readers of published knowledge, if any.</summary>
    public RecordRevision? PublishedRevision =>
        PublishedRevisionNumber is null
            ? null
            : _revisions.Single(revision => revision.Number == PublishedRevisionNumber.Value);

    /// <summary>
    /// The approval covering the current revision, or null when the current content has not been
    /// approved. An approval of earlier content never satisfies this.
    /// </summary>
    public Approval? ApprovalForCurrentRevision =>
        _approvals.LastOrDefault(approval => approval.ApprovedContentHash == CurrentRevision.ContentHash);

    public static KnowledgeRecord CreateDraft(
        KnowledgeRecordId id,
        ProjectScope scope,
        WorkItemId workItemId,
        RecordKind kind,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? frontMatter,
        Provenance provenance,
        DateTimeOffset createdAt,
        UserId createdBy)
    {
        var initialRevision = new RecordRevision(
            number: 1, title, body, frontMatter, provenance, createdAt, createdBy);

        return new KnowledgeRecord(id, scope, workItemId, kind, initialRevision, createdBy);
    }

    /// <summary>
    /// Appends a revision and returns the record to Draft. An earlier approval stays in the
    /// history but no longer covers the current content, so publication is blocked until a human
    /// reviews the new text.
    /// </summary>
    public RecordRevision AddRevision(
        string title,
        string body,
        IReadOnlyDictionary<string, string>? frontMatter,
        Provenance provenance,
        DateTimeOffset createdAt,
        UserId createdBy)
    {
        RequireNotArchived();

        var revision = new RecordRevision(
            CurrentRevision.Number + 1, title, body, frontMatter, provenance, createdAt, createdBy);

        _revisions.Add(revision);
        Status = RecordStatus.Draft;
        LastUpdatedAt = revision.CreatedAt;
        return revision;
    }

    public void SubmitForApproval(DateTimeOffset submittedAt)
    {
        RequireNotArchived();

        if (Status != RecordStatus.Draft)
        {
            throw new InvalidTransitionException(
                $"Only a draft can be submitted for approval; this record is {Status}.");
        }

        Status = RecordStatus.PendingApproval;
        LastUpdatedAt = Guard.Utc(submittedAt, nameof(submittedAt));
    }

    /// <summary>
    /// Records approval of a specific content hash. The caller passes the hash it actually read;
    /// if the record moved on in the meantime this throws rather than approving unseen content.
    /// </summary>
    public Approval Approve(UserId approverId, ContentHash approvedContentHash, DateTimeOffset approvedAt)
    {
        RequireNotArchived();

        if (Status != RecordStatus.PendingApproval)
        {
            throw new InvalidTransitionException(
                $"Only a record pending approval can be approved; this record is {Status}.");
        }

        if (approvedContentHash != CurrentRevision.ContentHash)
        {
            throw new ApprovalRevisionMismatchException(
                $"Approved content {approvedContentHash.ToShortString()} is not the current revision "
                + $"{CurrentRevision.ContentHash.ToShortString()}. Review the current revision and approve again.");
        }

        var approval = new Approval(
            approverId,
            approvedContentHash,
            CurrentRevision.Number,
            approvedAt,
            approverWasDraftCreator: approverId == CreatedBy);

        _approvals.Add(approval);
        Status = RecordStatus.Approved;
        LastUpdatedAt = approval.ApprovedAt;
        return approval;
    }

    public CorrectionRequest RequestCorrection(UserId requestedBy, string reason, DateTimeOffset requestedAt)
    {
        RequireNotArchived();

        if (Status is not (RecordStatus.PendingApproval or RecordStatus.Approved))
        {
            throw new InvalidTransitionException(
                $"A correction can only be requested while a record is pending approval or approved; "
                + $"this record is {Status}.");
        }

        var request = new CorrectionRequest(requestedBy, CurrentRevision.Number, reason, requestedAt);
        _correctionRequests.Add(request);
        Status = RecordStatus.Draft;
        LastUpdatedAt = request.RequestedAt;
        return request;
    }

    /// <summary>
    /// Publishes the current revision. Rejected unless an approval exists for exactly this
    /// content: an approval of an earlier revision is not sufficient, which is the entire point
    /// of control SB-23.
    /// </summary>
    public void Publish(DateTimeOffset publishedAt)
    {
        RequireNotArchived();

        if (Status != RecordStatus.Approved)
        {
            throw new InvalidTransitionException(
                $"Only an approved record can be published; this record is {Status}.");
        }

        if (ApprovalForCurrentRevision is null)
        {
            throw new ApprovalRevisionMismatchException(
                $"No approval covers revision {CurrentRevision.Number} "
                + $"({CurrentRevision.ContentHash.ToShortString()}). Publication requires approval of this exact content.");
        }

        Status = RecordStatus.Published;
        PublishedRevisionNumber = CurrentRevision.Number;
        LastUpdatedAt = Guard.Utc(publishedAt, nameof(publishedAt));
    }

    public void Archive(DateTimeOffset archivedAt)
    {
        RequireNotArchived();

        Status = RecordStatus.Archived;
        ArchivedAt = Guard.Utc(archivedAt, nameof(archivedAt));
        LastUpdatedAt = ArchivedAt.Value;
    }

    private void RequireNotArchived()
    {
        if (Status == RecordStatus.Archived)
        {
            throw new InvalidTransitionException("An archived record cannot be changed.");
        }
    }
}
