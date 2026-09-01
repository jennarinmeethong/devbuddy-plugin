using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Evidence;

/// <summary>
/// A pointer from a record to one stored evidence object, with a short note saying what it
/// shows. The note matters: a later owner reading a handover needs to know why an artefact
/// was attached without opening every one of them.
/// </summary>
public sealed record EvidenceReference
{
    public EvidenceReference(EvidenceObjectId evidenceObjectId, string description)
    {
        if (evidenceObjectId.Value == Guid.Empty)
        {
            throw new DomainValidationException("An evidence reference requires an evidence identifier.");
        }

        EvidenceObjectId = evidenceObjectId;
        Description = Guard.NotLongerThan(
            Guard.NotBlank(description, nameof(description)), 500, nameof(description));
    }

    public EvidenceObjectId EvidenceObjectId { get; }

    public string Description { get; }
}
