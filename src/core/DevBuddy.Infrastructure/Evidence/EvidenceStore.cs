using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Evidence;

/// <summary>
/// Evidence: metadata in PostgreSQL, bytes in the object store.
/// <para>
/// Stored content-addressed, so a substituted artefact is detectable and the same bytes stored
/// twice cost one copy. Newly stored evidence is NotScanned and therefore not releasable until
/// the Phase 6 scanner clears it (SB-17).
/// </para>
/// </summary>
internal sealed class EvidenceStore : IEvidenceStore
{
    private readonly EvidenceMetadataStore _metadata;
    private readonly IEvidenceBlobStore _blobs;
    private readonly IClock _clock;
    private readonly EvidenceStoreOptions _options;

    public EvidenceStore(
        EvidenceMetadataStore metadata,
        IEvidenceBlobStore blobs,
        IClock clock,
        IOptions<EvidenceStoreOptions> options)
    {
        _metadata = Guard.NotNull(metadata, nameof(metadata));
        _blobs = Guard.NotNull(blobs, nameof(blobs));
        _clock = Guard.NotNull(clock, nameof(clock));
        _options = Guard.NotNull(options, nameof(options)).Value;
    }

    public Task<EvidenceObject?> FindAsync(
        EvidenceObjectId id, ProjectScope scope, CancellationToken cancellationToken) =>
        _metadata.FindAsync(id, scope, cancellationToken);

    public Task<IReadOnlyList<EvidenceObject>> ListForScopeAsync(
        ProjectScope scope, CancellationToken cancellationToken) =>
        _metadata.ListForScopeAsync(scope, cancellationToken);

    public Task<Stream> OpenReadAsync(EvidenceObject evidence, CancellationToken cancellationToken)
    {
        Guard.NotNull(evidence, nameof(evidence));
        return _blobs.OpenReadAsync(evidence.Scope, evidence.StorageKey, cancellationToken);
    }

    public async Task<EvidenceObject> StoreAsync(
        ProjectScope scope,
        Stream content,
        string mediaType,
        UserId capturedBy,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(content, nameof(content));

        // Hashing needs the whole thing, so the size limit is enforced while buffering rather
        // than after: unbounded buffering is a denial of service anyone with an account can do.
        byte[] buffer = await ReadBoundedAsync(content, _options.MaxObjectBytes, cancellationToken);

        ContentHash hash = ContentHash.FromContent(Convert.ToBase64String(buffer));
        string storageKey = EvidenceStoreOptions.KeyFor(scope, hash);

        using (var upload = new MemoryStream(buffer, writable: false))
        {
            await _blobs.PutAsync(scope, storageKey, upload, mediaType, cancellationToken);
        }

        var evidence = new EvidenceObject(
            EvidenceObjectId.New(),
            scope,
            hash,
            mediaType,
            buffer.LongLength,
            storageKey,
            _clock.UtcNow,
            capturedBy);

        await _metadata.SaveAsync(evidence, cancellationToken);
        return evidence;
    }

    /// <summary>Records what the scanner concluded, which is what makes evidence releasable.</summary>
    public async Task<EvidenceObject> RecordScanResultAsync(
        EvidenceObject evidence, RedactionState state, CancellationToken cancellationToken)
    {
        Guard.NotNull(evidence, nameof(evidence));

        evidence.RecordScanResult(state, _clock.UtcNow);
        await _metadata.SaveAsync(evidence, cancellationToken);
        return evidence;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        long total = 0;

        while (true)
        {
            int read = await content.ReadAsync(chunk, cancellationToken);

            if (read == 0)
            {
                break;
            }

            total += read;

            if (total > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Evidence exceeds the {maxBytes} byte limit and was not stored.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }
}
