using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Security.Tests;

/// <summary>
/// SB-27 through the real enforcer: audit events, orphaned evidence, and stale backups actually
/// gone from their own copy, not only reported as due.
/// <para>
/// Three of the ten rows in the Phase 11 retention schedule are out of scope here on purpose, not
/// by oversight — see <c>RetentionService</c>'s summary for why draft staleness, exports, and
/// deleted-project purge are not exercised.
/// </para>
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class RetentionEnforcementTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task an_audit_event_past_its_window_is_deleted_and_a_recent_one_survives()
    {
        World world = await _fixture.CreateWorldAsync();
        using Session session = _fixture.OpenSession(world.Workspace);

        string oldReference = $"retention-old-{Guid.NewGuid():N}";
        string recentReference = $"retention-recent-{Guid.NewGuid():N}";

        IAuditSink sink = session.Resolve<IAuditSink>();

        await sink.WriteAsync(
            AuditEvent.ForWorkspace(
                AuditEventId.New(), world.Workspace, world.Founder, AuditAction.RecordViewed,
                AuditOutcome.Succeeded, oldReference, new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        await sink.WriteAsync(
            AuditEvent.ForWorkspace(
                AuditEventId.New(), world.Workspace, world.Founder, AuditAction.RecordViewed,
                AuditOutcome.Succeeded, recentReference, World.Now),
            CancellationToken.None);

        RetentionReport report = await session.Resolve<IRetentionEnforcer>().ApplyAsync(CancellationToken.None);

        Assert.True(report.AuditEventsDeleted >= 1);

        bool oldStillThere = await session.Db.AuditEvents
            .AnyAsync(entry => entry.ResourceReference == oldReference);
        bool recentStillThere = await session.Db.AuditEvents
            .AnyAsync(entry => entry.ResourceReference == recentReference);

        Assert.False(oldStillThere);
        Assert.True(recentStillThere);
    }

    [Fact]
    public async Task evidence_no_revision_references_is_purged_once_its_grace_period_passes()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId actor = await _fixture.CreateUserAsync($"retain-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, actor, Role.Reviewer, world.AlphaId);
        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-RET1", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        DevBuddyDbContext db = session.Db;

        EvidenceObjectId referencedId = EvidenceObjectId.New();
        EvidenceObjectId orphanedOldId = EvidenceObjectId.New();
        EvidenceObjectId orphanedRecentId = EvidenceObjectId.New();

        var longAgo = new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero);

        db.EvidenceObjects.AddRange(
            PlantedEvidence(referencedId, world, longAgo),
            PlantedEvidence(orphanedOldId, world, longAgo),
            PlantedEvidence(orphanedRecentId, world, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();

        // A record whose provenance names the first artefact. Old, but not orphaned: something
        // still cites it.
        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), world.Alpha, item.Id, RecordKind.TechnicalKnowledge,
            "Retention fixture", "Body.", frontMatter: null,
            new Provenance(
                ProvenanceSourceKind.HumanAuthored, "handover/retention", "Jennarin", World.Now,
                [new EvidenceReference(referencedId, "screenshot")]),
            World.Now, actor);

        await session.Resolve<IKnowledgeRepository>().AddRecordAsync(record, CancellationToken.None);

        RetentionReport report = await session.Resolve<IRetentionEnforcer>().ApplyAsync(CancellationToken.None);

        Assert.True(report.OrphanedEvidenceDeleted >= 1);

        Assert.True(await db.EvidenceObjects.IgnoreQueryFilters().AnyAsync(e => e.Id == referencedId.Value));
        Assert.False(await db.EvidenceObjects.IgnoreQueryFilters().AnyAsync(e => e.Id == orphanedOldId.Value));
        Assert.True(await db.EvidenceObjects.IgnoreQueryFilters().AnyAsync(e => e.Id == orphanedRecentId.Value));
    }

    [Fact]
    public async Task a_backup_past_its_window_is_deleted_and_a_recent_one_survives()
    {
        World world = await _fixture.CreateWorldAsync();
        using Session session = _fixture.OpenSession(world.Workspace);

        Directory.CreateDirectory(_fixture.BackupRoot);

        string oldBackup = Path.Combine(_fixture.BackupRoot, "backup-20150101-000000-" + Guid.NewGuid().ToString("N"));
        string recentBackup = Path.Combine(
            _fixture.BackupRoot,
            $"backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(oldBackup);
        Directory.CreateDirectory(recentBackup);

        RetentionReport report = await session.Resolve<IRetentionEnforcer>().ApplyAsync(CancellationToken.None);

        Assert.True(report.BackupsDeleted >= 1);
        Assert.False(Directory.Exists(oldBackup));
        Assert.True(Directory.Exists(recentBackup));
    }

    [Fact]
    public async Task an_export_past_its_window_is_deleted_and_a_recent_one_survives()
    {
        World world = await _fixture.CreateWorldAsync();
        using Session session = _fixture.OpenSession(world.Workspace);

        // Exports nest one project directory deep, unlike backups.
        string projectDirectory = Path.Combine(_fixture.ExportRoot, world.AlphaId.Value.ToString("N"));
        Directory.CreateDirectory(projectDirectory);

        string oldExport = Path.Combine(projectDirectory, "export-20150101-000000-" + Guid.NewGuid().ToString("N"));
        string recentExport = Path.Combine(
            projectDirectory,
            $"export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(oldExport);
        Directory.CreateDirectory(recentExport);

        RetentionReport report = await session.Resolve<IRetentionEnforcer>().ApplyAsync(CancellationToken.None);

        Assert.True(report.ExportsDeleted >= 1);
        Assert.False(Directory.Exists(oldExport));
        Assert.True(Directory.Exists(recentExport));
    }

    [Fact]
    public async Task an_archived_record_past_its_window_is_reported_eligible_but_not_deleted()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId actor = await _fixture.CreateUserAsync($"archive-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, actor, Role.Reviewer, world.AlphaId);
        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-RET2", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);
        DevBuddyDbContext db = session.Db;

        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), world.Alpha, item.Id, RecordKind.Decision,
            "Old archived note", "Body.", frontMatter: null,
            new Provenance(ProvenanceSourceKind.HumanAuthored, "handover/archive", "Jennarin", World.Now),
            World.Now, actor);

        await session.Resolve<IKnowledgeRepository>().AddRecordAsync(record, CancellationToken.None);

        await db.KnowledgeRecords
            .Where(row => row.Id == record.Id.Value)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, (int)RecordStatus.Archived)
                .SetProperty(row => row.ArchivedAt, new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        RetentionReport report = await session.Resolve<IRetentionEnforcer>().ApplyAsync(CancellationToken.None);

        Assert.True(report.ArchivedRecordsEligibleForDeletion >= 1);

        // Reported, not deleted: the schedule requires the owner's request first.
        Assert.True(await db.KnowledgeRecords.IgnoreQueryFilters().AnyAsync(row => row.Id == record.Id.Value));
    }

    private static EvidenceObjectRow PlantedEvidence(EvidenceObjectId id, World world, DateTimeOffset capturedAt) =>
        new()
        {
            Id = id.Value,
            WorkspaceId = world.Workspace.Value,
            ProjectId = world.AlphaId.Value,
            ContentHash = new string('a', 64),
            MediaType = "text/plain",
            SizeBytes = 3,
            StorageKey = $"retention/{id.Value:N}.bin",
            CapturedAt = capturedAt,
            CapturedBy = world.Founder.Value,
            RedactionState = (int)RedactionState.NotScanned,
        };
}
