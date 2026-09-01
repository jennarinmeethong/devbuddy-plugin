namespace DevBuddy.Domain.Knowledge;

/// <summary>
/// Where a record sits in the controlled lifecycle. Draft is a distinct status rather than a
/// flag, so that separating drafts from published knowledge (control SB-26) is something the
/// type system helps with rather than something every query has to remember.
/// </summary>
public enum RecordStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Published = 4,
    Archived = 5,
}
