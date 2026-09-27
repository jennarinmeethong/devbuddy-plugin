using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using DevBuddy.Domain.Common;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Evidence;

/// <summary>
/// Two checks the object store has to pass before any evidence goes in or comes out (ADR-0014).
/// <para>
/// <b>An unsigned request must be refused.</b> SeaweedFS, the shipped store since ADR-0014, allows
/// all access, anonymous included, when it has no credentials configured; MinIO refused to start
/// instead. Compose requires the credentials and the store's health probe checks the same thing,
/// but an operator can run any S3 service outside that Compose file. So the adapter asks once
/// without signing, and a store that answers is never written to or read from.
/// </para>
/// <para>
/// <b>The canary must decrypt.</b> SeaweedFS keeps its SSE-S3 key only in its environment, so a
/// mistyped key is not noticed when it starts: existing objects fail to read, and new ones are
/// written under the wrong key. The adapter writes one small encrypted object the first time and
/// reads it back every process after, so evidence cannot end up split across two keys.
/// </para>
/// <para>
/// Passing is remembered for the life of the process. Failing is not: every call checks again
/// and fails again until the store is fixed, and each failure says why.
/// </para>
/// </summary>
internal sealed class ObjectStoreSafety : IDisposable
{
    internal const string CanaryKey = "sse-canary";

    private static readonly byte[] CanaryContent =
        "DevBuddy evidence store canary. It exists so a wrong encryption key is noticed (ADR-0014)."u8.ToArray();

    private readonly IAmazonS3 _s3;
    private readonly EvidenceStoreOptions _options;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _verified;

    public ObjectStoreSafety(IAmazonS3 s3, IOptions<EvidenceStoreOptions> options)
        : this(s3, options, new SocketsHttpHandler())
    {
    }

    /// <summary>The handler is a seam for tests that need a store which answers anonymously.</summary>
    internal ObjectStoreSafety(IAmazonS3 s3, IOptions<EvidenceStoreOptions> options, HttpMessageHandler handler)
    {
        _s3 = Guard.NotNull(s3, nameof(s3));
        _options = Guard.NotNull(options, nameof(options)).Value;
        _http = new HttpClient(Guard.NotNull(handler, nameof(handler))) { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>The bucket the canary lives in. Never a workspace's: those are prefix plus a GUID.</summary>
    internal string CanaryBucket => $"{_options.BucketPrefix}-canary".ToLowerInvariant();

    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (_verified)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_verified)
            {
                return;
            }

            await RefuseUnsignedAsync(cancellationToken);

            if (_options.UseServerSideEncryption)
            {
                await CheckCanaryAsync(cancellationToken);
            }

            _verified = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }

    private async Task RefuseUnsignedAsync(CancellationToken cancellationToken)
    {
        // An unsigned ListBuckets. Only a refusal is safe; anything else, a 200 above all, means
        // the store would hand evidence to anybody who can reach it.
        Uri root = new(new Uri(_options.ServiceUrl.TrimEnd('/') + "/"), "/");
        using HttpResponseMessage response = await _http.GetAsync(root, cancellationToken);

        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized))
        {
            throw new EvidenceStoreUnsafeException(
                $"The evidence store at {root} answered an unsigned request with {(int)response.StatusCode}. "
                + "It would serve evidence to anyone who can reach it, so nothing is stored in it or read "
                + "from it. Configure its credentials (ADR-0014).");
        }
    }

    private async Task CheckCanaryAsync(CancellationToken cancellationToken)
    {
        string bucket = CanaryBucket;

        byte[]? stored = await ReadCanaryAsync(bucket, cancellationToken);
        if (stored is null)
        {
            // The first process to use this store: write the canary, then read it back like any
            // later process would, so the check below is the same check either way.
            await S3Buckets.EnsureAsync(_s3, bucket, cancellationToken);
            await _s3.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = CanaryKey,
                    InputStream = new MemoryStream(CanaryContent),
                    ContentType = "text/plain",
                    ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
                },
                cancellationToken);

            stored = await ReadCanaryAsync(bucket, cancellationToken);
        }

        if (stored is null || !stored.AsSpan().SequenceEqual(CanaryContent))
        {
            throw new EvidenceStoreUnsafeException(
                "The evidence store's canary did not read back as written. Nothing is stored or read.");
        }
    }

    /// <summary>The canary's bytes, or null if there is none yet. A failure to decrypt throws.</summary>
    private async Task<byte[]?> ReadCanaryAsync(string bucket, CancellationToken cancellationToken)
    {
        try
        {
            using GetObjectResponse response = await _s3.GetObjectAsync(bucket, CanaryKey, cancellationToken);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        catch (AmazonS3Exception missing) when (missing.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonS3Exception failed)
        {
            // SeaweedFS answers 500 for an object it cannot decrypt with the key it was given.
            throw new EvidenceStoreUnsafeException(
                $"The evidence store could not read its canary ({(int)failed.StatusCode} {failed.ErrorCode}). "
                + "The most likely cause is a different encryption key from the one the store was first "
                + "started with (DEVBUDDY_EVIDENCE_SSE_KEK). Nothing is stored or read until it is fixed.",
                failed);
        }
    }
}

/// <summary>The object store failed a safety check; evidence is neither written to it nor read from it.</summary>
public sealed class EvidenceStoreUnsafeException : InvalidOperationException
{
    public EvidenceStoreUnsafeException(string message)
        : base(message)
    {
    }

    public EvidenceStoreUnsafeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
