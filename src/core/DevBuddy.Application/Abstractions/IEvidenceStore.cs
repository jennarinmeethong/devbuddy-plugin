using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Evidence metadata and bytes. MinIO is the default implementation (ADR-0004).
/// <para>
/// There is deliberately no method that returns a shareable link. Evidence is streamed by the
/// API after an authorization check, so attachments are covered by the same isolation tests as
/// records (control SB-12).
/// </para>
/// </summary>
public interface IEvidenceStore
{
    Task<EvidenceObject?> FindAsync(
        EvidenceObjectId id, ProjectScope scope, CancellationToken cancellationToken);

    Task<IReadOnlyList<EvidenceObject>> ListForScopeAsync(
        ProjectScope scope, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(EvidenceObject evidence, CancellationToken cancellationToken);

    Task<EvidenceObject> StoreAsync(
        ProjectScope scope,
        Stream content,
        string mediaType,
        UserId capturedBy,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records what the scanner concluded, which is what makes stored bytes releasable.
    /// <para>
    /// Stored evidence begins <see cref="RedactionState.NotScanned"/> and
    /// the download use case refuses to release it in that state, so without this
    /// the store could be written to and never read from. The implementation has existed since
    /// Phase 3; it was missing from this port, which is why nothing could call it.
    /// </para>
    /// </summary>
    Task<EvidenceObject> RecordScanResultAsync(
        EvidenceObject evidence, RedactionState state, CancellationToken cancellationToken);
}
