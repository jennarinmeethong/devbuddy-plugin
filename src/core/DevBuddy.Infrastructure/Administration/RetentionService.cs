using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Administration;

/// <summary>
/// One retention pass, across every copy the schedule names that this build can actually reach
/// (SB-27).
/// <para>
/// Three rules run here: audit events past their window are deleted outright; evidence no
/// revision references, past its grace period, is deleted from both the database and the object
/// store; and backups past the configured window are deleted from disk — the specific gap
/// `docs/plan.md` calls out as recorded but not enforced.
/// </para>
/// <para>
/// Three rows in the schedule are not implemented here, on purpose rather than by omission.
/// Draft staleness is already the Phase 6 quality check's job (<c>DetectStaleness</c>), not a
/// deletion. Exports carry no stored artefact yet — <c>ExportAsync</c> returns a manifest and
/// writes nothing to disk — so there is nothing to purge until that changes. A project cannot yet
/// be deleted at all, so the deleted-project purge has no trigger to run from. All three are
/// named as residual risk rather than silently skipped; see the verification matrix.
/// </para>
/// </summary>
internal sealed class RetentionService : IRetentionEnforcer
{
    private readonly DevBuddyDbContext _db;
    private readonly IEvidenceBlobStore _blobs;
    private readonly IClock _clock;
    private readonly RetentionOptions _retention;
    private readonly BackupOptions _backup;

    public RetentionService(
        DevBuddyDbContext db,
        IEvidenceBlobStore blobs,
        IClock clock,
        IOptions<RetentionOptions> retention,
        IOptions<BackupOptions> backup)
    {
        _db = Guard.NotNull(db, nameof(db));
        _blobs = Guard.NotNull(blobs, nameof(blobs));
        _clock = Guard.NotNull(clock, nameof(clock));
        _retention = Guard.NotNull(retention, nameof(retention)).Value;
        _backup = Guard.NotNull(backup, nameof(backup)).Value;
    }

    public async Task<RetentionReport> ApplyAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;

        int auditEventsDeleted = await PurgeAuditEventsAsync(now, cancellationToken);
        int evidenceDeleted = await PurgeOrphanedEvidenceAsync(now, cancellationToken);
        int backupsDeleted = PurgeBackups(now);
        int archivedEligible = await CountArchivedRecordsEligibleAsync(now, cancellationToken);

        return new RetentionReport(auditEventsDeleted, evidenceDeleted, backupsDeleted, archivedEligible);
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
    /// Directory names carry the moment the backup was taken (<see cref="BackupService"/>), so
    /// that is what ages a backup out rather than filesystem metadata a copy or a restore could
    /// have changed.
    /// </summary>
    private int PurgeBackups(DateTimeOffset now)
    {
        string root = Path.GetFullPath(_backup.RootPath);

        if (!Directory.Exists(root))
        {
            return 0;
        }

        DateTimeOffset cutoff = now - _backup.Retention;
        int deleted = 0;

        foreach (string directory in Directory.GetDirectories(root))
        {
            if (BackupTimestamp(Path.GetFileName(directory)) is { } takenAt && takenAt < cutoff)
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

    /// <summary>Parses the <c>backup-yyyyMMdd-HHmmss-...</c> name <see cref="BackupService"/> writes.</summary>
    private static DateTimeOffset? BackupTimestamp(string directoryName)
    {
        string[] parts = directoryName.Split('-');

        if (parts.Length < 3 || parts[0] != "backup")
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
