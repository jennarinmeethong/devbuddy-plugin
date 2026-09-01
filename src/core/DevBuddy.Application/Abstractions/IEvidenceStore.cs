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
}
