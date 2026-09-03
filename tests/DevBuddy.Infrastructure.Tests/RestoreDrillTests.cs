using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Administration;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Mapping;
using DevBuddy.Infrastructure.Persistence.Repositories;
using DevBuddy.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The destroy-and-restore drill: the Phase 10 exit criterion, run rather than described.
/// <para>
/// A backup nobody has restored is a belief, not a control. This one writes a real installation to
/// a real PostgreSQL and a real MinIO, takes a backup, destroys the database completely, migrates
/// an empty one, restores, and then checks that the records, the accounts, and — the part most
/// often missed — the evidence bytes all came back.
/// </para>
/// <para>
/// Destroying means a different database, created and migrated from nothing. Deleting rows would
/// leave sequences, indexes, and anything a migration created, which is not what a lost volume
/// looks like.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RestoreDrillTests(PostgresFixture postgres, MinioFixture minio)
    : IClassFixture<MinioFixture>
{
    private static CancellationToken Ct => CancellationToken.None;

    private readonly PostgresFixture _postgres = postgres;
    private readonly MinioFixture _minio = minio;

    [Fact]
    public async Task an_installation_survives_losing_its_database_entirely()
    {
        string backupRoot = Path.Combine(
            Path.GetTempPath(), "devbuddy-drill-" + Guid.NewGuid().ToString("N"));

        try
        {
            // 1. An installation with something in it worth losing.
            Seed seed = await Seed.CreateAsync(_postgres);
            byte[] artefact = Encoding.UTF8.GetBytes("2026-09-01 importer failed at step 3\n");

            (KnowledgeRecordId recordId, EvidenceObjectId evidenceId, string storageKey) =
                await PopulateAsync(seed, artefact);

            // 2. Back it up.
            BackupManifest manifest;

            await using (DevBuddyDbContext context = _postgres.CreateContext(seed.Workspace))
            {
                manifest = await ServiceFor(context, backupRoot).BackupAsync(Ct);
            }

            Assert.True(manifest.SizeBytes > 0);

            // 3. Lose the database. Not "delete the rows" — a different database, migrated from
            //    nothing, which is what replacing a container with an empty volume actually is.
            string replacement = await _postgres.CreateIsolatedDatabaseAsync(
                "drill_" + Guid.NewGuid().ToString("N")[..16]);

            await using (DevBuddyDbContext empty = PostgresFixture.CreateContextFor(replacement, null))
            {
                Assert.Empty(await empty.Workspaces.AsNoTracking().ToListAsync(Ct));
            }

            // 4. Restore into it.
            RestoreOutcome outcome;

            await using (DevBuddyDbContext context = PostgresFixture.CreateContextFor(replacement, null))
            {
                outcome = await ServiceFor(context, backupRoot).RestoreAsync(manifest.Reference, Ct);
            }

            Assert.True(outcome.Succeeded, outcome.Detail);

            // 5. Everything is back, including the bytes.
            await using (DevBuddyDbContext restored =
                PostgresFixture.CreateContextFor(replacement, seed.Workspace))
            {
                KnowledgeRecord? record = await new KnowledgeRepository(restored)
                    .FindRecordAsync(recordId, seed.Alpha, Ct);

                Assert.NotNull(record);
                Assert.Equal(RecordStatus.Published, record!.Status);
                Assert.Equal("Rollback is a migration, not a restore", record.CurrentRevision.Title);

                // The approval survived with the hash it was bound to, which is what makes the
                // restored record still provably approved rather than merely present (SB-23).
                Assert.Equal(
                    record.CurrentRevision.ContentHash.Value,
                    record.ApprovalForCurrentRevision!.ApprovedContentHash.Value);

                Assert.NotEmpty(await restored.Users.AsNoTracking().ToListAsync(Ct));
                Assert.NotEmpty(await restored.AuditEvents.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct));

                EvidenceObject? evidence = await new EvidenceMetadataStore(restored)
                    .FindAsync(evidenceId, seed.Alpha, Ct);

                Assert.NotNull(evidence);
                Assert.Equal(storageKey, evidence!.StorageKey);
            }

            // And the bytes themselves, read back out of the object store the same way a reader
            // would. A restore that returned the rows and left the artefacts behind would look
            // like a success and hand every reader a broken link.
            await using Stream bytes = await Blobs().OpenReadAsync(seed.Alpha, storageKey, Ct);
            using var read = new MemoryStream();
            await bytes.CopyToAsync(read, Ct);

            Assert.Equal(artefact, read.ToArray());
        }
        finally
        {
            if (Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Restoring over a populated installation is refused.
    /// <para>
    /// Merging would half-succeed on every conflicting identifier and leave a mixture nobody could
    /// reason about. Refusing is the only answer that leaves the operator with a system they can
    /// still describe.
    /// </para>
    /// </summary>
    [Fact]
    public async Task a_restore_into_an_installation_that_already_has_data_is_refused()
    {
        string backupRoot = Path.Combine(
            Path.GetTempPath(), "devbuddy-drill-" + Guid.NewGuid().ToString("N"));

        try
        {
            Seed seed = await Seed.CreateAsync(_postgres);

            await using DevBuddyDbContext context = _postgres.CreateContext(seed.Workspace);
            BackupService service = ServiceFor(context, backupRoot);

            BackupManifest manifest = await service.BackupAsync(Ct);
            RestoreOutcome outcome = await service.RestoreAsync(manifest.Reference, Ct);

            Assert.False(outcome.Succeeded);
            Assert.Contains("already has data", outcome.Detail, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task a_reference_nobody_issued_is_refused_rather_than_half_applied()
    {
        string backupRoot = Path.Combine(
            Path.GetTempPath(), "devbuddy-drill-" + Guid.NewGuid().ToString("N"));

        Seed seed = await Seed.CreateAsync(_postgres);
        await using DevBuddyDbContext context = _postgres.CreateContext(seed.Workspace);

        RestoreOutcome outcome = await ServiceFor(context, backupRoot)
            .RestoreAsync("backup-that-never-existed", Ct);

        Assert.False(outcome.Succeeded);
        Assert.Contains("No backup", outcome.Detail, StringComparison.Ordinal);
    }

    /// <summary>Writes a published record with an approval, and an artefact beside it.</summary>
    private async Task<(KnowledgeRecordId Record, EvidenceObjectId Evidence, string StorageKey)>
        PopulateAsync(Seed seed, byte[] artefact)
    {
        await using DevBuddyDbContext context = _postgres.CreateContext(seed.Workspace);

        var workItem = new WorkItem(
            WorkItemId.New(), seed.Alpha, $"CRQ-{Guid.NewGuid():N}"[..12],
            WorkItemType.ChangeRequest, "Rollback safety", "So a rollback is planned, not improvised.",
            Seed.Now, seed.Author);

        context.WorkItems.Add(RowMappers.ToRow(workItem));
        await context.SaveChangesAsync(Ct);

        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), seed.Alpha, workItem.Id, RecordKind.Decision,
            "Rollback is a migration, not a restore",
            "Rolling back a migration is itself a migration, and is planned as one.",
            frontMatter: null,
            new Provenance(ProvenanceSourceKind.HumanAuthored, "meeting/2026-09-01", "a person", Seed.Now),
            Seed.Now, seed.Author);

        record.SubmitForApproval(Seed.Now.AddMinutes(1));
        record.Approve(seed.Author, record.CurrentRevision.ContentHash, Seed.Now.AddMinutes(2));
        record.Publish(Seed.Now.AddMinutes(3));

        await new KnowledgeRepository(context).AddRecordAsync(record, Ct);

        EvidenceObject stored = await StoreFor(context).StoreAsync(
            seed.Alpha, new MemoryStream(artefact), "text/plain", seed.Author, Ct);

        // An audit entry, because the audit trail is the part of a restore nobody checks and the
        // part a later owner most needs: a system recovered without its history cannot say who
        // approved what.
        await new AuditStore(context).WriteAsync(
            AuditEvent.ForProject(
                AuditEventId.New(), seed.Alpha, seed.Author, AuditAction.RecordPublished,
                AuditOutcome.Succeeded, record.Id.ToString(), Seed.Now.AddMinutes(3)),
            Ct);

        return (record.Id, stored.Id, stored.StorageKey);
    }

    private BackupService ServiceFor(DevBuddyDbContext context, string backupRoot) =>
        new(context, Blobs(), new SystemClock(), Options.Create(new BackupOptions { RootPath = backupRoot }));

    private EvidenceStore StoreFor(DevBuddyDbContext context) =>
        new(new EvidenceMetadataStore(context), Blobs(), new SystemClock(), Settings());

    private ObjectStorageEvidenceBlobStore Blobs()
    {
        IOptions<EvidenceStoreOptions> options = Settings();

        var client = new AmazonS3Client(
            new BasicAWSCredentials(options.Value.AccessKey, options.Value.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = options.Value.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
            });

        return new ObjectStorageEvidenceBlobStore(client, options);
    }

    private IOptions<EvidenceStoreOptions> Settings() => Options.Create(new EvidenceStoreOptions
    {
        Provider = EvidenceStoreProvider.ObjectStorage,
        ServiceUrl = _minio.ServiceUrl,
        AccessKey = MinioFixture.AccessKey,
        SecretKey = MinioFixture.SecretKey,

        // The test container has no KMS. Encryption stays on by default for a real deployment.
        UseServerSideEncryption = false,
    });
}
