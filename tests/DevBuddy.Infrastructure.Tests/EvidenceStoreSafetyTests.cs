using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Evidence;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Volumes;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The adapter's two safety checks (ADR-0014), each against a real SeaweedFS configured the way
/// that makes the check matter.
/// <para>
/// SeaweedFS with no credentials serves everything to anyone, and SeaweedFS with a different
/// encryption key starts normally and fails only when an old object is read. Neither is visible
/// until evidence has already gone somewhere it should not, so each test here would pass against
/// an adapter without the check only by writing evidence into the unsafe store, and asserts that
/// nothing arrived.
/// </para>
/// </summary>
public sealed class EvidenceStoreSafetyTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task a_store_that_answers_unsigned_requests_is_never_written_to_or_read_from()
    {
        // No credentials at all: SeaweedFS's open default, which the Compose file forbids.
        await using IContainer open = await EvidenceStoreFixture.StartAsync(
            accessKey: null, secretKey: null, EvidenceStoreFixture.Kek, volume: null);

        string url = EvidenceStoreFixture.ServiceUrlOf(open);
        IOptions<EvidenceStoreOptions> options = Settings(url);
        using AmazonS3Client client = Client(url);
        using var safety = new ObjectStoreSafety(client, options);
        var blobs = new ObjectStorageEvidenceBlobStore(client, options, safety);
        ProjectScope scope = new(WorkspaceId.New(), ProjectId.New());

        await Assert.ThrowsAsync<EvidenceStoreUnsafeException>(() => blobs.PutAsync(
            scope, "a/b/c", new MemoryStream("evidence"u8.ToArray()), "text/plain", Ct));
        await Assert.ThrowsAsync<EvidenceStoreUnsafeException>(() => blobs.OpenReadAsync(scope, "a/b/c", Ct));

        // Nothing went in: this store lists its buckets to anyone, which is the point.
        ListBucketsResponse buckets = await client.ListBucketsAsync(Ct);
        Assert.Empty(buckets.Buckets ?? []);
    }

    [Fact]
    public async Task a_different_encryption_key_is_refused_before_anything_is_stored()
    {
        IVolume volume = new VolumeBuilder().WithName($"devbuddy-evidence-safety-{Guid.NewGuid():N}").Build();
        await volume.CreateAsync();

        try
        {
            ProjectScope scope = new(WorkspaceId.New(), ProjectId.New());
            byte[] first = Encoding.UTF8.GetBytes("stored under the first key");

            // First start: the canary is written under the configured key, and evidence with it.
            await using (IContainer store = await StartOn(volume, EvidenceStoreFixture.Kek))
            {
                (ObjectStorageEvidenceBlobStore blobs, ObjectStoreSafety safety, AmazonS3Client client) = Adapter(store);
                using (safety)
                using (client)
                {
                    await blobs.PutAsync(scope, "first", new MemoryStream(first), "text/plain", Ct);
                }
            }

            // The same volume with another key. SeaweedFS starts as if nothing were wrong.
            const string other = "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100";
            await using (IContainer store = await StartOn(volume, other))
            {
                (ObjectStorageEvidenceBlobStore blobs, ObjectStoreSafety safety, AmazonS3Client client) = Adapter(store);
                using (safety)
                using (client)
                {
                    await Assert.ThrowsAsync<EvidenceStoreUnsafeException>(() => blobs.PutAsync(
                        scope, "second", new MemoryStream("under the wrong key"u8.ToArray()), "text/plain", Ct));

                    // Nothing was written under the wrong key.
                    ListObjectsV2Response listed = await client.ListObjectsV2Async(
                        new ListObjectsV2Request { BucketName = Settings("unused").Value.BucketFor(scope.WorkspaceId) }, Ct);
                    Assert.DoesNotContain(listed.S3Objects ?? [], item => item.Key == "second");
                }
            }

            // The right key again, and everything reads back.
            await using (IContainer store = await StartOn(volume, EvidenceStoreFixture.Kek))
            {
                (ObjectStorageEvidenceBlobStore blobs, ObjectStoreSafety safety, AmazonS3Client client) = Adapter(store);
                using (safety)
                using (client)
                {
                    await using Stream read = await blobs.OpenReadAsync(scope, "first", Ct);
                    using var buffer = new MemoryStream();
                    await read.CopyToAsync(buffer, Ct);
                    Assert.Equal(first, buffer.ToArray());
                }
            }
        }
        finally
        {
            await volume.DeleteAsync();
        }
    }

    [Fact]
    public async Task an_unsigned_request_that_is_refused_is_the_only_answer_accepted()
    {
        // Against a stand-in that answers every request, with no container: the check reads the
        // status, not the store's product name. 404 is not a refusal either.
        foreach (HttpStatusCode answer in (HttpStatusCode[])[HttpStatusCode.OK, HttpStatusCode.NotFound])
        {
            IOptions<EvidenceStoreOptions> options = Settings("http://evidence.invalid:8333");
            using AmazonS3Client client = Client("http://evidence.invalid:8333");
            using var safety = new ObjectStoreSafety(client, options, new Answering(answer));

            await Assert.ThrowsAsync<EvidenceStoreUnsafeException>(() => safety.EnsureAsync(Ct));
        }
    }

    private static async Task<IContainer> StartOn(IVolume volume, string kek) =>
        await EvidenceStoreFixture.StartAsync(
            EvidenceStoreFixture.AccessKey, EvidenceStoreFixture.SecretKey, kek, volume.Name);

    private static (ObjectStorageEvidenceBlobStore, ObjectStoreSafety, AmazonS3Client) Adapter(IContainer store)
    {
        string url = EvidenceStoreFixture.ServiceUrlOf(store);
        IOptions<EvidenceStoreOptions> options = Settings(url);
        AmazonS3Client client = Client(url);
        var safety = new ObjectStoreSafety(client, options);
        return (new ObjectStorageEvidenceBlobStore(client, options, safety), safety, client);
    }

    private static IOptions<EvidenceStoreOptions> Settings(string url) => Options.Create(new EvidenceStoreOptions
    {
        Provider = EvidenceStoreProvider.ObjectStorage,
        ServiceUrl = url,
        AccessKey = EvidenceStoreFixture.AccessKey,
        SecretKey = EvidenceStoreFixture.SecretKey,
        UseServerSideEncryption = true,
    });

    private static AmazonS3Client Client(string url) => new(
        new BasicAWSCredentials(EvidenceStoreFixture.AccessKey, EvidenceStoreFixture.SecretKey),
        new AmazonS3Config { ServiceURL = url, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });

    private sealed class Answering(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
