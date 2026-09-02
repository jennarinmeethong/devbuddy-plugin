using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Testcontainers.Minio;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// A real MinIO. ADR-0004 makes object storage the default evidence store, so the default is what
/// gets tested; covering only the filesystem fallback would prove the adapter we do not ship.
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    private readonly MinioContainer _container = new MinioBuilder("minio/minio:RELEASE.2025-04-22T22-12-26Z")
        .WithUsername("devbuddy")
        .WithPassword("devbuddy-test-only")
        .Build();

    public string ServiceUrl => _container.GetConnectionString();

    public static string AccessKey => "devbuddy";

    public static string SecretKey => "devbuddy-test-only";

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>
/// Evidence: metadata in PostgreSQL, bytes in the object store, content-addressed, and not
/// releasable until it has been scanned.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EvidenceStoreTests(PostgresFixture postgres, MinioFixture minio)
    : IClassFixture<MinioFixture>
{
    private readonly PostgresFixture _postgres = postgres;
    private readonly MinioFixture _minio = minio;

    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task evidence_round_trips_through_object_storage()
    {
        Seed seed = await Seed.CreateAsync(_postgres);
        await using DevBuddyDbContext context = _postgres.CreateContext(seed.Workspace);

        EvidenceStore store = ObjectStorageStore(context);
        byte[] content = Encoding.UTF8.GetBytes("2026-09-01 importer failed at step 3\n");

        EvidenceObject stored = await store.StoreAsync(
            seed.Alpha, new MemoryStream(content), "text/plain", seed.Author, Ct);

        Assert.Equal(content.LongLength, stored.SizeBytes);
        Assert.Equal(ContentHash.FromContent(Convert.ToBase64String(content)), stored.ContentHash);

        // Content-addressed under a project prefix, so one project cannot guess another key and
        // a substituted artefact changes its address.
        Assert.StartsWith(seed.Alpha.ProjectId.Value.ToString(), stored.StorageKey, StringComparison.Ordinal);

        await using Stream read = await store.OpenReadAsync(stored, Ct);
        using var buffer = new MemoryStream();
        await read.CopyToAsync(buffer, Ct);

        Assert.Equal(content, buffer.ToArray());
    }

    [Fact]
    public async Task newly_stored_evidence_is_not_releasable_until_it_has_been_scanned()
    {
        Seed seed = await Seed.CreateAsync(_postgres);
        await using DevBuddyDbContext context = _postgres.CreateContext(seed.Workspace);

        EvidenceStore store = ObjectStorageStore(context);

        EvidenceObject stored = await store.StoreAsync(
            seed.Alpha, Bytes("config with a token in it"), "text/plain", seed.Author, Ct);

        // Control SB-17: unscanned material is not safe by default. The scanner arrives in
        // Phase 6; until then nothing is releasable, which is the correct failure direction.
        Assert.Equal(RedactionState.NotScanned, stored.RedactionState);
        Assert.False(stored.IsReleasable);

        EvidenceObject scanned = await store.RecordScanResultAsync(stored, RedactionState.Redacted, Ct);
        Assert.True(scanned.IsReleasable);

        EvidenceObject? reloaded = await new EvidenceMetadataStore(context)
            .FindAsync(stored.Id, seed.Alpha, Ct);

        Assert.NotNull(reloaded);
        Assert.Equal(RedactionState.Redacted, reloaded.RedactionState);
        Assert.NotNull(reloaded.ScannedAt);
    }

    [Fact]
    public async Task identical_content_stored_twice_lands_on_the_same_key()
    {
        Seed seed = await Seed.CreateAsync(_postgres);
        await using DevBuddyDbContext context = _postgres.CreateContext(seed.Workspace);

        EvidenceStore store = ObjectStorageStore(context);

        EvidenceObject first = await store.StoreAsync(
            seed.Alpha, Bytes("the same log line"), "text/plain", seed.Author, Ct);

        EvidenceObject second = await store.StoreAsync(
            seed.Alpha, Bytes("the same log line"), "text/plain", seed.Author, Ct);

        // Two metadata records, one copy of the bytes.
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.StorageKey, second.StorageKey);
    }

    [Fact]
    public async Task the_same_content_in_two_projects_does_not_share_a_key()
    {
        Seed seed = await Seed.CreateAsync(_postgres);
        await using DevBuddyDbContext context = _postgres.CreateContext(seed.Workspace);

        EvidenceStore store = ObjectStorageStore(context);

        EvidenceObject inAlpha = await store.StoreAsync(
            seed.Alpha, Bytes("shared text"), "text/plain", seed.Author, Ct);

        EvidenceObject inBeta = await store.StoreAsync(
            seed.Beta, Bytes("shared text"), "text/plain", seed.Author, Ct);

        // Deduplication stops at the project boundary. Sharing a key across projects would make
        // a deletion in one project silently affect the other.
        Assert.Equal(inAlpha.ContentHash, inBeta.ContentHash);
        Assert.NotEqual(inAlpha.StorageKey, inBeta.StorageKey);

        var metadata = new EvidenceMetadataStore(context);
        Assert.Equal(inAlpha.Id, Assert.Single(await metadata.ListForScopeAsync(seed.Alpha, Ct)).Id);
        Assert.Equal(inBeta.Id, Assert.Single(await metadata.ListForScopeAsync(seed.Beta, Ct)).Id);
    }

    [Fact]
    public async Task an_oversized_object_is_refused_and_nothing_is_written()
    {
        Seed seed = await Seed.CreateAsync(_postgres);
        await using DevBuddyDbContext context = _postgres.CreateContext(seed.Workspace);

        EvidenceStore store = ObjectStorageStore(context, options => options.MaxObjectBytes = 64);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.StoreAsync(seed.Alpha, Bytes(new string('x', 500)), "text/plain", seed.Author, Ct));

        // An upload limit is an availability control before it is a storage-cost one: hashing
        // needs buffering, and unbounded buffering is a denial of service (SB-21).
        Assert.Contains("limit", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await new EvidenceMetadataStore(context).ListForScopeAsync(seed.Alpha, Ct));
    }

    [Fact]
    public async Task the_filesystem_fallback_round_trips_and_refuses_a_path_that_escapes()
    {
        Seed seed = await Seed.CreateAsync(_postgres);
        await using DevBuddyDbContext context = _postgres.CreateContext(seed.Workspace);

        string root = Path.Combine(Path.GetTempPath(), "devbuddy-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            IOptions<EvidenceStoreOptions> options = Options.Create(new EvidenceStoreOptions
            {
                Provider = EvidenceStoreProvider.FileSystem,
                RootPath = root,
            });

            var blobs = new FileSystemEvidenceBlobStore(options);
            var store = new EvidenceStore(
                new EvidenceMetadataStore(context), blobs, new FixedClock(), options);

            EvidenceObject stored = await store.StoreAsync(
                seed.Alpha, Bytes("single host install"), "text/plain", seed.Author, Ct);

            await using (Stream read = await store.OpenReadAsync(stored, Ct))
            {
                using var reader = new StreamReader(read);
                Assert.Equal("single host install", await reader.ReadToEndAsync(Ct));
            }

            // The key is derived internally, never supplied by a caller. A path check that only
            // holds while that stays true is not a check (SB-05).
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => blobs.OpenReadAsync(seed.Alpha, "../../../etc/passwd", Ct));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private EvidenceStore ObjectStorageStore(
        DevBuddyDbContext context, Action<EvidenceStoreOptions>? configure = null)
    {
        var settings = new EvidenceStoreOptions
        {
            Provider = EvidenceStoreProvider.ObjectStorage,
            ServiceUrl = _minio.ServiceUrl,
            AccessKey = MinioFixture.AccessKey,
            SecretKey = MinioFixture.SecretKey,

            // MinIO in the test container has no KMS configured, so server-side encryption is
            // off here. It stays on by default for a real deployment.
            UseServerSideEncryption = false,
        };

        configure?.Invoke(settings);

        IOptions<EvidenceStoreOptions> options = Options.Create(settings);

        var client = new AmazonS3Client(
            new BasicAWSCredentials(settings.AccessKey, settings.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = settings.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
            });

        return new EvidenceStore(
            new EvidenceMetadataStore(context),
            new ObjectStorageEvidenceBlobStore(client, options),
            new FixedClock(),
            options);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Seed.Now;
    }
}
