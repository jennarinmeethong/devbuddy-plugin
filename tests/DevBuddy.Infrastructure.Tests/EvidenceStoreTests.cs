using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Repositories;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using Microsoft.Extensions.Options;
using Testcontainers.Minio;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// A real MinIO. ADR-0004 makes object storage the default evidence store, so the default is what
/// gets tested; covering only the filesystem fallback would prove the adapter we do not ship.
/// <para>
/// Built from <c>docker/evidence/Dockerfile</c>, the image <c>docker/compose.yaml</c> builds, so the
/// image tested is the image shipped. Since 2026-09-25 that Dockerfile compiles MinIO from its source
/// (<c>info.md</c>). Docker Hub stopped serving <c>minio/minio</c> by 2026-09-13 and quay.io stopped
/// serving it without a login on 2026-09-24, and each time every test here failed on the pull. The
/// image is built once per test run and shared by every class that starts a container from it.
/// </para>
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    private static readonly Lazy<Task<IFutureDockerImage>> Image = new(BuildImageAsync);

    private MinioContainer? _container;

    public string ServiceUrl => _container!.GetConnectionString();

    public static string AccessKey => "devbuddy";

    public static string SecretKey => "devbuddy-test-only";

    public async Task InitializeAsync()
    {
        _container = new MinioBuilder(await Image.Value)
            .WithUsername(AccessKey)
            .WithPassword(SecretKey)
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static async Task<IFutureDockerImage> BuildImageAsync()
    {
        IFutureDockerImage image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(Path.Combine(RepositoryRoot(), "docker", "evidence"))
            .WithDockerfile("Dockerfile")
            .WithName("devbuddy-evidence-test:source")
            .WithDeleteIfExists(false)
            .Build();

        await image.CreateAsync();
        return image;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !directory.EnumerateFiles("DevBuddy.slnx").Any())
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root (DevBuddy.slnx) was not found.");
    }
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
    public async Task concurrent_first_captures_in_a_new_workspace_all_succeed_and_the_bucket_stays_private()
    {
        const int Racers = 6;
        Seed seed = await Seed.CreateAsync(_postgres);

        // Every capture lists the buckets before any of them creates one, so each finds the new
        // workspace's bucket missing and asks for it. That is the interleaving the e2e suite hit
        // on 2026-09-17 by chance, made certain: the losers used to throw
        // BucketAlreadyOwnedByYou and the capture answered 500.
        RacingS3Client client = RacingClient(Racers);

        Task<EvidenceObject>[] captures = Enumerable.Range(0, Racers)
            .Select(async racer =>
            {
                await using DevBuddyDbContext context = _postgres.CreateContext(seed.Workspace);
                EvidenceStore store = ObjectStorageStore(context, client: client);

                return await store.StoreAsync(
                    seed.Alpha, Bytes($"first capture {racer}"), "text/plain", seed.Author, Ct);
            })
            .ToArray();

        EvidenceObject[] stored = await Task.WhenAll(captures);

        // Without this the test could pass by never racing at all.
        Assert.Equal(Racers, client.BucketCreationAttempts);

        await using (DevBuddyDbContext context = _postgres.CreateContext(seed.Workspace))
        {
            EvidenceStore store = ObjectStorageStore(context);
            Assert.Equal(Racers, (await new EvidenceMetadataStore(context).ListForScopeAsync(seed.Alpha, Ct)).Count);

            foreach (EvidenceObject evidence in stored)
            {
                await using Stream read = await store.OpenReadAsync(evidence, Ct);
                using var reader = new StreamReader(read);
                Assert.StartsWith("first capture ", await reader.ReadToEndAsync(Ct), StringComparison.Ordinal);
            }
        }

        // Tolerating the race must not have made the bucket readable by anybody who is not the
        // API: no listing and no object without credentials.
        string bucket = new EvidenceStoreOptions().BucketFor(seed.Workspace);
        using var anonymous = new HttpClient();

        using HttpResponseMessage listing = await anonymous.GetAsync(
            new Uri($"{_minio.ServiceUrl.TrimEnd('/')}/{bucket}/"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, listing.StatusCode);

        using HttpResponseMessage download = await anonymous.GetAsync(
            new Uri($"{_minio.ServiceUrl.TrimEnd('/')}/{bucket}/{stored[0].StorageKey}"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, download.StatusCode);
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
        DevBuddyDbContext context,
        Action<EvidenceStoreOptions>? configure = null,
        IAmazonS3? client = null)
    {
        var settings = new EvidenceStoreOptions
        {
            Provider = EvidenceStoreProvider.ObjectStorage,
            ServiceUrl = _minio.ServiceUrl,
            AccessKey = MinioFixture.AccessKey,
            SecretKey = MinioFixture.SecretKey,

            // This container has no KMS, so encryption is off for these tests. Note what that
            // does and does not prove: it exercises the store, not the shipped configuration.
            // The claim that used to sit here — that it "stays on by default for a real
            // deployment" — was true of the default and false of the deployment, because
            // docker/compose.yaml ran MinIO without a KMS key too. Every evidence upload against
            // the shipped stack failed with a 500 until that was fixed, and DeploymentTests now
            // checks the compose file rather than leaving it to a comment.
            UseServerSideEncryption = false,
        };

        configure?.Invoke(settings);

        IOptions<EvidenceStoreOptions> options = Options.Create(settings);

        client ??= new AmazonS3Client(Credentials, ClientConfig());

        return new EvidenceStore(
            new EvidenceMetadataStore(context),
            new ObjectStorageEvidenceBlobStore(client, options),
            new FixedClock(),
            options);
    }

    private static BasicAWSCredentials Credentials => new(MinioFixture.AccessKey, MinioFixture.SecretKey);

    private AmazonS3Config ClientConfig() => new()
    {
        ServiceURL = _minio.ServiceUrl,
        ForcePathStyle = true,
        AuthenticationRegion = "us-east-1",
    };

    private RacingS3Client RacingClient(int racers) => new(racers, Credentials, ClientConfig());

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Seed.Now;
    }

    /// <summary>
    /// A real client that holds each of the first <c>racers</c> bucket listings until all of them
    /// have been answered, so every one of those callers sees the same missing bucket.
    /// </summary>
    private sealed class RacingS3Client(int racers, AWSCredentials credentials, AmazonS3Config config)
        : AmazonS3Client(credentials, config)
    {
        private readonly TaskCompletionSource _allListed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _listed;
        private int _creations;

        public int BucketCreationAttempts => _creations;

        public override async Task<ListBucketsResponse> ListBucketsAsync(
            CancellationToken cancellationToken = default)
        {
            ListBucketsResponse response = await base.ListBucketsAsync(cancellationToken);
            int listed = Interlocked.Increment(ref _listed);

            if (listed == racers)
            {
                _allListed.TrySetResult();
            }

            if (listed <= racers)
            {
                await _allListed.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return response;
        }

        public override Task<PutBucketResponse> PutBucketAsync(
            PutBucketRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _creations);
            return base.PutBucketAsync(request, cancellationToken);
        }
    }
}
