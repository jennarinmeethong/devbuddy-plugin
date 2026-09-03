using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Administration;

/// <summary>
/// One retention pass, across every copy the schedule names (SB-27).
/// <para>
/// Audit events past their window are deleted outright; evidence no revision references, past
/// its grace period, is deleted from both the database and the object store; backups past their
/// configured window are deleted from disk — the specific gap `docs/plan.md` once called out as
/// recorded but not enforced; and exports past their window are deleted the same way, now that
/// <see cref="ExportService"/> writes an actual copy instead of only a manifest.
/// </para>
/// <para>
/// Two rows in the schedule are still not implemented here, on purpose rather than by omission.
/// Draft staleness is already the Phase 6 quality check's job (<c>DetectStaleness</c>), not a
/// deletion. A deleted-project purge is unnecessary rather than missing: deleting a project
/// (<c>DeleteProjectUseCase</c>) already removes everything scoped to it immediately, so there is
/// no lagging state for a sweep to catch up on. Application log retention is a container
/// log-driver setting, outside this codebase entirely. See the release-readiness note.
/// </para>
/// </summary>
internal sealed class RetentionService : IRetentionEnforcer
{
    private readonly DevBuddyDbContext _db;
    private readonly IEvidenceBlobStore _blobs;
    private readonly IClock _clock;
    private readonly RetentionOptions _retention;
    private readonly BackupOptions _backup;
    private readonly ExportOptions _export;

    public RetentionService(
        DevBuddyDbContext db,
        IEvidenceBlobStore blobs,
        IClock clock,
        IOptions<RetentionOptions> retention,
        IOptions<BackupOptions> backup,
        IOptions<ExportOptions> export)
    {
        _db = Guard.NotNull(db, nameof(db));
        _blobs = Guard.NotNull(blobs, nameof(blobs));
        _clock = Guard.NotNull(clock, nameof(clock));
        _retention = Guard.NotNull(retention, nameof(retention)).Value;
        _backup = Guard.NotNull(backup, nameof(backup)).Value;
        _export = Guard.NotNull(export, nameof(export)).Value;
    }

    public async Task<RetentionReport> ApplyAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;

        int auditEventsDeleted = await PurgeAuditEventsAsync(now, cancellationToken);
        int evidenceDeleted = await PurgeOrphanedEvidenceAsync(now, cancellationToken);
        int backupsDeleted = PurgeTimestampedDirectories(_backup.RootPath, "backup", _backup.Retention, now);
        int exportsDeleted = PurgeExports(now);
        int archivedEligible = await CountArchivedRecordsEligibleAsync(now, cancellationToken);

        return new RetentionReport(
            auditEventsDeleted, evidenceDeleted, backupsDeleted, exportsDeleted, archivedEligible);
    }

    /// <summary>Deleted outright. The schedule gives audit events no exception and no report step.</summary>
    private async Task<int> PurgeAuditEventsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = now - _retention.AuditEventRetention;

        return await _db.AuditEvents
            .Where(auditEvent => auditEvent.OccurredAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// An evidence object is orphaned when no revision's provenance names it. Computed in memory
    /// rather than as a server-side join: the reference lives inside a jsonb column, or in the AI
    /// data policy this is not, and a self-hosted installation's revision count is small enough
    /// that reading every provenance once per sweep is the honest cost of that trade.
    /// </summary>
    private async Task<int> PurgeOrphanedEvidenceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = now - _retention.EvidenceOrphanGracePeriod;

        HashSet<Guid> referenced = [.. (await _db.RecordRevisions
                .AsNoTracking()
                .Select(revision => revision.Provenance)
                .ToListAsync(cancellationToken))
            .SelectMany(provenance => provenance.Evidence)
            .Select(reference => reference.EvidenceObjectId)];

        List<EvidenceObjectRow> candidates = await _db.EvidenceObjects
            .IgnoreQueryFilters()
            .Where(evidence => evidence.CapturedAt < cutoff)
            .ToListAsync(cancellationToken);

        int deleted = 0;

        foreach (EvidenceObjectRow candidate in candidates)
        {
            if (referenced.Contains(candidate.Id))
            {
                continue;
            }

            var scope = new ProjectScope(
                new WorkspaceId(candidate.WorkspaceId), new ProjectId(candidate.ProjectId));

            await _blobs.DeleteAsync(scope, candidate.StorageKey, cancellationToken);
            _db.EvidenceObjects.Remove(candidate);
            deleted++;
        }

        if (deleted > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return deleted;
    }

    /// <summary>
    /// Every export directory is one project subdirectory deep — <c>{root}/{projectId}/{export
    /// directory}</c> — so a project's exports can be found without reading every other
    /// project's, unlike backups, which are one flat directory of the whole installation.
    /// </summary>
    private int PurgeExports(DateTimeOffset now)
    {
        string root = Path.GetFullPath(_export.RootPath);

        if (!Directory.Exists(root))
        {
            return 0;
        }

        int deleted = 0;

        foreach (string projectDirectory in Directory.GetDirectories(root))
        {
            deleted += PurgeTimestampedDirectories(projectDirectory, "export", _export.Retention, now);
        }

        return deleted;
    }

    /// <summary>
    /// Directory names carry the moment the copy was taken (<see cref="BackupService"/>,
    /// <see cref="ExportService"/>), so that is what ages one out rather than filesystem metadata
    /// a copy or a restore could have changed.
    /// </summary>
    private static int PurgeTimestampedDirectories(
        string rootPath, string prefix, TimeSpan retention, DateTimeOffset now)
    {
        string root = Path.GetFullPath(rootPath);

        if (!Directory.Exists(root))
        {
            return 0;
        }

        DateTimeOffset cutoff = now - retention;
        int deleted = 0;

        foreach (string directory in Directory.GetDirectories(root))
        {
            if (TimestampOf(Path.GetFileName(directory), prefix) is { } takenAt && takenAt < cutoff)
            {
                Directory.Delete(directory, recursive: true);
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>Reports only. The schedule deletes an eligible archived record on owner request, not on a sweep.</summary>
    private async Task<int> CountArchivedRecordsEligibleAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = now - _retention.ArchivedRecordEligibility;

        return await _db.KnowledgeRecords
            .IgnoreQueryFilters()
            .CountAsync(record => record.ArchivedAt != null && record.ArchivedAt < cutoff, cancellationToken);
    }

    /// <summary>
    /// Parses the <c>{prefix}-yyyyMMdd-HHmmss-...</c> name <see cref="BackupService"/> and
    /// <see cref="ExportService"/> write.
    /// </summary>
    private static DateTimeOffset? TimestampOf(string directoryName, string prefix)
    {
        string[] parts = directoryName.Split('-');

        if (parts.Length < 3 || parts[0] != prefix)
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(
            $"{parts[1]}-{parts[2]}",
            "yyyyMMdd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed)
            ? parsed
            : null;
    }
}
