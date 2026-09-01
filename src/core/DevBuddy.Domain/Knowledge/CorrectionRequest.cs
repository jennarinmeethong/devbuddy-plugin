using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Knowledge;

/// <summary>
/// A reviewer sending a record back for changes, with the reason retained. The reason is part
/// of the knowledge: it explains why the next revision exists.
/// </summary>
public sealed record CorrectionRequest
{
    public CorrectionRequest(
        UserId requestedBy,
        int targetRevisionNumber,
        string reason,
        DateTimeOffset requestedAt)
    {
        if (targetRevisionNumber < 1)
        {
            throw new DomainValidationException(
                "A correction request must reference a revision number of 1 or greater.");
        }

        RequestedBy = requestedBy;
        TargetRevisionNumber = targetRevisionNumber;
        Reason = Guard.NotLongerThan(Guard.NotBlank(reason, nameof(reason)), 4000, nameof(reason));
        RequestedAt = Guard.Utc(requestedAt, nameof(requestedAt));
    }

    public UserId RequestedBy { get; }

    public int TargetRevisionNumber { get; }

    public string Reason { get; }

    public DateTimeOffset RequestedAt { get; }
}
