using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Domain.Evidence;

/// <summary>
/// Metadata for one stored artefact: a log, a screenshot, an export. The bytes live in object
/// storage (MinIO by default, ADR-0004); this aggregate is the record of what they are, which
/// project they belong to, and whether they have been cleared for release.
/// </summary>
public sealed class EvidenceObject
{
    public EvidenceObject(
        EvidenceObjectId id,
        ProjectScope scope,
        ContentHash contentHash,
        string mediaType,
        long sizeBytes,
        string storageKey,
        DateTimeOffset capturedAt,
        UserId capturedBy)
    {
        if (sizeBytes < 0)
        {
            throw new DomainValidationException("Evidence size cannot be negative.");
        }

        Id = id;
        Scope = scope;
        ContentHash = contentHash;
        MediaType = Guard.NotLongerThan(Guard.NotBlank(mediaType, nameof(mediaType)), 200, nameof(mediaType));
        SizeBytes = sizeBytes;
        StorageKey = Guard.NotLongerThan(Guard.NotBlank(storageKey, nameof(storageKey)), 1000, nameof(storageKey));
        CapturedAt = Guard.Utc(capturedAt, nameof(capturedAt));
        CapturedBy = capturedBy;
        RedactionState = RedactionState.NotScanned;
    }

    public EvidenceObjectId Id { get; }

    public ProjectScope Scope { get; }

    /// <summary>Content address of the stored bytes, so a substitution is detectable.</summary>
    public ContentHash ContentHash { get; }

    public string MediaType { get; }

    public long SizeBytes { get; }

    public string StorageKey { get; }

    public DateTimeOffset CapturedAt { get; }

    public UserId CapturedBy { get; }

    public RedactionState RedactionState { get; private set; }

    public DateTimeOffset? ScannedAt { get; private set; }

    /// <summary>
    /// Whether this artefact may be returned to a caller. Unscanned material is not releasable,
    /// so the safe answer is the default rather than something a caller has to remember.
    /// </summary>
    public bool IsReleasable =>
        RedactionState is RedactionState.Clean or RedactionState.Redacted;

    public void RecordScanResult(RedactionState state, DateTimeOffset scannedAt)
    {
        Guard.Defined(state, nameof(state));

        if (state == RedactionState.NotScanned)
        {
            throw new DomainValidationException("A scan result cannot be NotScanned.");
        }

        RedactionState = state;
        ScannedAt = Guard.Utc(scannedAt, nameof(scannedAt));
    }
}
