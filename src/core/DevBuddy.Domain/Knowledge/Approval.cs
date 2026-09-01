using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Knowledge;

/// <summary>
/// A human approval bound to the exact content that was reviewed.
/// <para>
/// info.md permits a draft creator to approve their own draft, subject to the same permission
/// check as any reviewer, and requires the audit history to record the approver, the exact
/// approved revision, the timestamp, and whether the approver was also the creator. All four
/// are captured here, and ApproverWasDraftCreator is computed by the aggregate rather than
/// supplied by a caller.
/// </para>
/// </summary>
public sealed record Approval
{
    public Approval(
        UserId approverId,
        ContentHash approvedContentHash,
        int approvedRevisionNumber,
        DateTimeOffset approvedAt,
        bool approverWasDraftCreator)
    {
        if (approvedRevisionNumber < 1)
        {
            throw new DomainValidationException("An approval must reference a revision number of 1 or greater.");
        }

        ApproverId = approverId;
        ApprovedContentHash = approvedContentHash;
        ApprovedRevisionNumber = approvedRevisionNumber;
        ApprovedAt = Guard.Utc(approvedAt, nameof(approvedAt));
        ApproverWasDraftCreator = approverWasDraftCreator;
    }

    public UserId ApproverId { get; }

    /// <summary>The content the approver actually read. Publication compares against this.</summary>
    public ContentHash ApprovedContentHash { get; }

    public int ApprovedRevisionNumber { get; }

    public DateTimeOffset ApprovedAt { get; }

    /// <summary>
    /// Recorded rather than prevented. Self-approval is permitted by info.md; concealing it
    /// would not be.
    /// </summary>
    public bool ApproverWasDraftCreator { get; }
}
