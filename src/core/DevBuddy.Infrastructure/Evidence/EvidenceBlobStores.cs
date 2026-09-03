using Amazon.S3;
using Amazon.S3.Model;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Evidence;

/// <summary>
/// The bytes half of evidence storage. Metadata stays in PostgreSQL, which remains the source of
/// truth; this only moves content in and out.
/// </summary>
internal interface IEvidenceBlobStore
{
    Task PutAsync(
        ProjectScope scope, string storageKey, Stream content, string mediaType, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(ProjectScope scope, string storageKey, CancellationToken cancellationToken);

    /// <summary>Removes the bytes. Missing already counts as removed (SB-27).</summary>
    Task DeleteAsync(ProjectScope scope, string storageKey, CancellationToken cancellationToken);
}

/// <summary>
/// S3-compatible object storage, MinIO by default (ADR-0004).
/// <para>
/// There is deliberately no method that issues a presigned URL. Evidence is streamed back through
/// the API after an authorization check, so attachments are covered by the same isolation tests
/// as records (SB-12). A presigned link would be an access path outside the pipeline.
/// </para>
/// </summary>
internal sealed class ObjectStorageEvidenceBlobStore : IEvidenceBlobStore
{
    private readonly IAmazonS3 _s3;
    private readonly EvidenceStoreOptions _options;

    public ObjectStorageEvidenceBlobStore(IAmazonS3 s3, IOptions<EvidenceStoreOptions> options)
    {
        _s3 = Guard.NotNull(s3, nameof(s3));
        _options = Guard.NotNull(options, nameof(options)).Value;
    }

    public async Task PutAsync(
        ProjectScope scope,
        string storageKey,
        Stream content,
        string mediaType,
        CancellationToken cancellationToken)
    {
        string bucket = _options.BucketFor(scope.WorkspaceId);
        await EnsureBucketAsync(bucket, cancellationToken);

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = storageKey,
            InputStream = content,
            ContentType = mediaType,
        };

        if (_options.UseServerSideEncryption)
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
        }

        await _s3.PutObjectAsync(request, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(
        ProjectScope scope, string storageKey, CancellationToken cancellationToken)
    {
        GetObjectResponse response = await _s3.GetObjectAsync(
            _options.BucketFor(scope.WorkspaceId), storageKey, cancellationToken);

        return response.ResponseStream;
    }

    public async Task DeleteAsync(
        ProjectScope scope, string storageKey, CancellationToken cancellationToken) =>
        await _s3.DeleteObjectAsync(
            _options.BucketFor(scope.WorkspaceId), storageKey, cancellationToken);

    private async Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken)
    {
        ListBucketsResponse buckets = await _s3.ListBucketsAsync(cancellationToken);

        if (buckets.Buckets?.Exists(existing => existing.BucketName == bucket) == true)
        {
            return;
        }

        // Created private. There is no public-read path to evidence, ever.
        await _s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken);
    }
}

/// <summary>
/// Local files. Kept behind the same interface for tests and single-host installations; the
/// default deployment uses object storage.
/// </summary>
internal sealed class FileSystemEvidenceBlobStore : IEvidenceBlobStore
{
    private readonly EvidenceStoreOptions _options;

    public FileSystemEvidenceBlobStore(IOptions<EvidenceStoreOptions> options) =>
        _options = Guard.NotNull(options, nameof(options)).Value;

    public async Task PutAsync(
        ProjectScope scope,
        string storageKey,
        Stream content,
        string mediaType,
        CancellationToken cancellationToken)
    {
        string path = PathFor(scope, storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (File.Exists(path))
        {
            // Content-addressed: the same bytes produce the same key, so a repeat write is a
            // duplicate rather than a conflict.
            return;
        }

        await using FileStream file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream> OpenReadAsync(
        ProjectScope scope, string storageKey, CancellationToken cancellationToken)
    {
        string path = PathFor(scope, storageKey);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Evidence {storageKey} is not in the store.", path);
        }

        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public Task DeleteAsync(ProjectScope scope, string storageKey, CancellationToken cancellationToken)
    {
        string path = PathFor(scope, storageKey);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves the path and refuses anything that escapes the workspace directory. The key is
    /// derived internally rather than supplied by a caller, but a path check that only holds
    /// while that stays true is not a check (SB-05).
    /// </summary>
    private string PathFor(ProjectScope scope, string storageKey)
    {
        string root = Path.GetFullPath(
            Path.Combine(_options.RootPath, _options.BucketFor(scope.WorkspaceId)));

        string full = Path.GetFullPath(Path.Combine(root, storageKey));

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(full, root, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"Evidence key {storageKey} resolves outside the workspace store.");
        }

        return full;
    }
}
