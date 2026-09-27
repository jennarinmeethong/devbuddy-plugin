using System.Globalization;
using System.Text.Json;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Administration;

/// <summary>
/// Takes and restores a backup: every row, and every stored artefact beside them.
/// <para>
/// Both halves matter, and a backup of only the database is the mistake this is shaped to avoid.
/// Records reference evidence by storage key; a restore that put the rows back and left the bytes
/// behind would produce a system that looks recovered and hands a reader a broken link for every
/// screenshot and log they try to open.
/// </para>
/// </summary>
internal sealed class BackupService
{
    private readonly DevBuddyDbContext _db;
    private readonly IEvidenceBlobStore _blobs;
    private readonly IClock _clock;
    private readonly BackupOptions _options;

    private readonly IProjectDirectory? _projects;
    private readonly FileDeletionLedger? _ledger;

    public BackupService(
        DevBuddyDbContext db,
        IEvidenceBlobStore blobs,
        IClock clock,
        IOptions<BackupOptions> options,
        IProjectDirectory? projects = null,
        FileDeletionLedger? ledger = null)
    {
        _projects = projects;
        _ledger = ledger;
        _db = Guard.NotNull(db, nameof(db));
        _blobs = Guard.NotNull(blobs, nameof(blobs));
        _clock = Guard.NotNull(clock, nameof(clock));
        _options = Guard.NotNull(options, nameof(options)).Value;
    }

    public async Task<BackupManifest> BackupAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;

        // Sortable, unique, and safe as a directory name: the moment it was taken, then enough
        // randomness that two backups in the same second cannot collide.
        string reference = string.Create(
            CultureInfo.InvariantCulture,
            $"backup-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}")[..40];

        DirectoryInfo directory = Directory.CreateDirectory(Path.Combine(Root(), reference));

        BackupArchive archive = await ReadEverythingAsync(now, cancellationToken);

        string rows = Path.Combine(directory.FullName, "rows.json");

        await using (FileStream file = File.Create(rows))
        {
            await JsonSerializer.SerializeAsync(file, archive, ArchiveJson.Options, cancellationToken);
        }

        long evidenceBytes = await CopyEvidenceOutAsync(archive, directory, cancellationToken);

        return new BackupManifest(
            reference,
            new FileInfo(rows).Length + evidenceBytes,
            now);
    }

    /// <summary>
    /// Puts a backup back, into an empty installation.
    /// <para>
    /// Empty is required rather than preferred. Merging a backup into a populated database would
    /// half-succeed on every conflicting identifier and leave a mixture nobody could reason about,
    /// so a restore that finds anything already there refuses and says so. That is also exactly
    /// the shape of the drill: destroy, migrate, restore.
    /// </para>
    /// </summary>
    public async Task<RestoreOutcome> RestoreAsync(string reference, CancellationToken cancellationToken)
    {
        (BackupArchive? archive, DirectoryInfo directory, string? refusal) =
            await ReadArchiveAsync(reference, cancellationToken);

        if (archive is null)
        {
            return new RestoreOutcome(reference ?? string.Empty, false, refusal!);
        }

        string schema = await CurrentSchemaVersionAsync(cancellationToken);

        if (!string.Equals(archive.SchemaVersion, schema, StringComparison.Ordinal))
        {
            // Refused rather than attempted. Rows written against one schema and read into
            // another is the failure that looks like success until somebody opens a record.
            return new RestoreOutcome(
                reference!,
                false,
                $"The backup was taken at schema {archive.SchemaVersion} and this database is at "
                + $"{schema}. Migrate to the matching version and restore again.");
        }

        if (await _db.Workspaces.AsNoTracking().AnyAsync(cancellationToken))
        {
            return new RestoreOutcome(
                reference!,
                false,
                "This installation already has data. Restore into an empty database.");
        }

        await WriteEverythingAsync(archive, cancellationToken);
        int artefacts = await CopyEvidenceBackAsync(archive, directory, cancellationToken);
        string deletions = await ReapplyDeletionsAsync(archive.CreatedAt, cancellationToken);

        return new RestoreOutcome(
            reference!,
            true,
            $"Restored {archive.KnowledgeRecords.Count} records, {archive.WorkItems.Count} work "
            + $"items, {archive.Users.Count} accounts, and {artefacts} artefacts. Everyone signs "
            + $"in again: sessions are not part of a backup. {deletions}");
    }

    /// <summary>
    /// Writes a backup's evidence bytes into the configured store, beside rows this database
    /// already holds, and changes no row (ADR-0014).
    /// <para>
    /// This is how an installation moves its evidence to a new store: back up on the old release,
    /// upgrade, and run this against the new, empty store. A whole restore cannot do it, because it
    /// refuses a database that has data, and emptying the database only to put the same rows back
    /// would sign everybody out and give up the old volume as a way back.
    /// </para>
    /// <para>
    /// Only an artefact whose row is still here, under the same identifier, scope and storage key,
    /// is written, and only if its bytes still hash to the row's content hash. It succeeds only if
    /// every evidence row ends up with its bytes; otherwise it says how many did not and why, and
    /// can be run again, since writing the same content to the same key changes nothing.
    /// </para>
    /// </summary>
    public async Task<RestoreOutcome> RestoreEvidenceAsync(string reference, CancellationToken cancellationToken)
    {
        (BackupArchive? archive, DirectoryInfo directory, string? refusal) =
            await ReadArchiveAsync(reference, cancellationToken);

        if (archive is null)
        {
            return new RestoreOutcome(reference ?? string.Empty, false, refusal!);
        }

        if (!await _db.Workspaces.AsNoTracking().AnyAsync(cancellationToken))
        {
            return new RestoreOutcome(
                reference!,
                false,
                "This installation has no data. Evidence is restored only beside the rows it belongs "
                + "to; restore the whole backup into an empty installation instead.");
        }

        Dictionary<Guid, EvidenceObjectRow> current = await _db.EvidenceObjects
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(row => row.Id, cancellationToken);

        var evidence = new DirectoryInfo(Path.Combine(directory.FullName, "evidence"));
        var restored = new HashSet<Guid>();
        int gone = 0;
        int altered = 0;

        foreach (EvidenceObjectRow artefact in archive.EvidenceObjects)
        {
            if (!current.TryGetValue(artefact.Id, out EvidenceObjectRow? row)
                || row.WorkspaceId != artefact.WorkspaceId
                || row.ProjectId != artefact.ProjectId
                || !string.Equals(row.StorageKey, artefact.StorageKey, StringComparison.Ordinal))
            {
                // Deleted since the backup, or not the same artefact any more. Not written back.
                gone++;
                continue;
            }

            var file = new FileInfo(Path.Combine(evidence.FullName, FileNameFor(artefact)));

            if (!file.Exists)
            {
                continue;
            }

            byte[] bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
            string hash = ContentHash.FromContent(Convert.ToBase64String(bytes)).Value;

            if (!string.Equals(hash, row.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                altered++;
                continue;
            }

            var scope = new ProjectScope(new WorkspaceId(row.WorkspaceId), new ProjectId(row.ProjectId));
            await _blobs.PutAsync(scope, row.StorageKey, new MemoryStream(bytes), row.MediaType, cancellationToken);
            restored.Add(row.Id);
        }

        int without = current.Count - restored.Count;
        string detail = string.Create(
            CultureInfo.InvariantCulture,
            $"Restored the bytes of {restored.Count} of {current.Count} evidence objects. No row was changed.");

        if (gone > 0)
        {
            detail += string.Create(
                CultureInfo.InvariantCulture,
                $" {gone} artefact(s) in the backup belong to evidence no longer here and were not written.");
        }

        if (altered > 0)
        {
            detail += string.Create(
                CultureInfo.InvariantCulture,
                $" {altered} artefact(s) did not match their content hash and were not written.");
        }

        if (without > 0)
        {
            detail += string.Create(
                CultureInfo.InvariantCulture,
                $" {without} evidence object(s) still have no bytes in this store: captured after the backup, or not in it. Take a new backup on the old store and run this again.");
        }

        return new RestoreOutcome(reference!, without == 0, detail);
    }

    /// <summary>The backup's archive, or why it cannot be used.</summary>
    private async Task<(BackupArchive? Archive, DirectoryInfo Directory, string? Refusal)> ReadArchiveAsync(
        string? reference, CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(Path.Combine(Root(), reference ?? string.Empty));

        if (!directory.Exists)
        {
            return (null, directory, "No backup with that reference.");
        }

        var rows = new FileInfo(Path.Combine(directory.FullName, "rows.json"));

        if (!rows.Exists)
        {
            return (null, directory, "The backup is missing its rows.");
        }

        BackupArchive? archive;

        await using (FileStream file = rows.OpenRead())
        {
            archive = await JsonSerializer.DeserializeAsync<BackupArchive>(file, ArchiveJson.Options, cancellationToken);
        }

        if (archive is null)
        {
            return (null, directory, "The backup could not be read.");
        }

        if (archive.Version != BackupArchive.CurrentVersion)
        {
            return (null, directory,
                $"The backup is version {archive.Version} and this build reads version "
                + $"{BackupArchive.CurrentVersion}.");
        }

        return (archive, directory, null);
    }

    /// <summary>
    /// Deletes again every project the ledger says was deleted after this backup was taken
    /// (Phase 13, D7), with the same deletion <c>delete_project</c> performs, bytes included.
    /// A missing ledger does not stop the restore; it is reported, because a restore that quietly
    /// brought deleted projects back is the thing this exists to prevent.
    /// </summary>
    private async Task<string> ReapplyDeletionsAsync(DateTimeOffset takenAt, CancellationToken cancellationToken)
    {
        if (_ledger is null || _projects is null)
        {
            return "Deletions made after this backup were not checked.";
        }

        LedgerRead ledger = await _ledger.ReadAsync(cancellationToken);

        if (!ledger.Exists)
        {
            return "No deletion ledger was found beside the backups, so projects deleted after this "
                + "backup was taken could not be deleted again. Check whether any were.";
        }

        List<DeletionEntry> later = [.. ledger.Entries.Where(entry => entry.DeletedAt > takenAt)];

        foreach (DeletionEntry entry in later)
        {
            await _projects.DeleteProjectAsync(
                new ProjectScope(new WorkspaceId(entry.WorkspaceId), new ProjectId(entry.ProjectId)),
                cancellationToken);
        }

        string unreadable = ledger.Unreadable == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" {ledger.Unreadable} ledger line(s) could not be read.");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Deleted again {later.Count} project(s) the ledger records as deleted after this backup.{unreadable}");
    }

    private string Root() => Path.GetFullPath(_options.RootPath);

    private async Task<string> CurrentSchemaVersionAsync(CancellationToken cancellationToken)
    {
        IEnumerable<string> applied = await _db.Database.GetAppliedMigrationsAsync(cancellationToken);
        return applied.LastOrDefault() ?? "none";
    }

    /// <summary>
    /// Every table, unfiltered.
    /// <para>
    /// Sessions are left out deliberately: refresh and recovery tokens are short-lived and
    /// restoring them would resurrect whatever was signed in at the moment the backup was taken.
    /// Passwords and machine tokens are kept, because those are how people and their plugins get
    /// back in.
    /// </para>
    /// </summary>
    private async Task<BackupArchive> ReadEverythingAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        return new BackupArchive
        {
            CreatedAt = now,
            SchemaVersion = await CurrentSchemaVersionAsync(cancellationToken),

            Workspaces = await _db.Workspaces.AsNoTracking().ToListAsync(cancellationToken),
            Teams = await _db.Teams.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken),
            Projects = await _db.Projects.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken),
            SourceRepositories = await _db.SourceRepositories.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken),
            DeploymentEnvironments = await _db.DeploymentEnvironments.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken),
            SourceSnapshots = await _db.SourceSnapshots.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken),
            Users = await _db.Users.AsNoTracking().ToListAsync(cancellationToken),
            Memberships = await _db.Memberships.AsNoTracking().ToListAsync(cancellationToken),
            AiAccessPolicies = await _db.AiAccessPolicies.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken),
            UserCredentials = await _db.UserCredentials.AsNoTracking().ToListAsync(cancellationToken),
            MachineTokens = await _db.MachineTokens.AsNoTracking().ToListAsync(cancellationToken),
            WorkItems = await _db.WorkItems.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken),

            KnowledgeRecords = await _db.KnowledgeRecords
                .IgnoreQueryFilters()
                .Include(record => record.Revisions)
                .Include(record => record.Approvals)
                .Include(record => record.Corrections)
                .AsNoTracking()
                .ToListAsync(cancellationToken),

            EvidenceObjects = await _db.EvidenceObjects.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken),
            AuditEvents = await _db.AuditEvents.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken),
        };
    }

    /// <summary>
    /// Writes the rows back, parents before children, in one save.
    /// <para>
    /// Knowledge records carry their revisions, approvals, and corrections as owned collections, so
    /// adding the record adds them too — which is also why the read above has to Include them.
    /// </para>
    /// </summary>
    private async Task WriteEverythingAsync(BackupArchive archive, CancellationToken cancellationToken)
    {
        _db.Workspaces.AddRange(archive.Workspaces);
        _db.Users.AddRange(archive.Users);
        _db.UserCredentials.AddRange(archive.UserCredentials);
        _db.MachineTokens.AddRange(archive.MachineTokens);
        _db.Teams.AddRange(archive.Teams);
        _db.Projects.AddRange(archive.Projects);
        _db.Memberships.AddRange(archive.Memberships);
        _db.AiAccessPolicies.AddRange(archive.AiAccessPolicies);
        _db.SourceRepositories.AddRange(archive.SourceRepositories);
        _db.DeploymentEnvironments.AddRange(archive.DeploymentEnvironments);
        _db.SourceSnapshots.AddRange(archive.SourceSnapshots);
        _db.WorkItems.AddRange(archive.WorkItems);
        _db.KnowledgeRecords.AddRange(archive.KnowledgeRecords);
        _db.EvidenceObjects.AddRange(archive.EvidenceObjects);
        _db.AuditEvents.AddRange(archive.AuditEvents);

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<long> CopyEvidenceOutAsync(
        BackupArchive archive, DirectoryInfo directory, CancellationToken cancellationToken)
    {
        DirectoryInfo evidence = directory.CreateSubdirectory("evidence");
        long bytes = 0;

        foreach (EvidenceObjectRow artefact in archive.EvidenceObjects)
        {
            var scope = new ProjectScope(
                new WorkspaceId(artefact.WorkspaceId), new ProjectId(artefact.ProjectId));

            string target = Path.Combine(evidence.FullName, FileNameFor(artefact));

            try
            {
                await using Stream source = await _blobs.OpenReadAsync(
                    scope, artefact.StorageKey, cancellationToken);

                await using FileStream file = File.Create(target);
                await source.CopyToAsync(file, cancellationToken);
                bytes += file.Length;
            }
            catch (FileNotFoundException)
            {
                // A row whose bytes are already gone. Recorded by its absence rather than by
                // failing the whole backup: the rest of the installation is still worth keeping,
                // and the restore reports how many artefacts came back.
                continue;
            }
        }

        return bytes;
    }

    private async Task<int> CopyEvidenceBackAsync(
        BackupArchive archive, DirectoryInfo directory, CancellationToken cancellationToken)
    {
        var evidence = new DirectoryInfo(Path.Combine(directory.FullName, "evidence"));

        if (!evidence.Exists)
        {
            return 0;
        }

        int restored = 0;

        foreach (EvidenceObjectRow artefact in archive.EvidenceObjects)
        {
            var file = new FileInfo(Path.Combine(evidence.FullName, FileNameFor(artefact)));

            if (!file.Exists)
            {
                continue;
            }

            var scope = new ProjectScope(
                new WorkspaceId(artefact.WorkspaceId), new ProjectId(artefact.ProjectId));

            await using FileStream source = file.OpenRead();
            await _blobs.PutAsync(scope, artefact.StorageKey, source, artefact.MediaType, cancellationToken);
            restored++;
        }

        return restored;
    }

    /// <summary>
    /// A flat file name per artefact, keyed by its identifier rather than its storage key.
    /// <para>
    /// Storage keys contain slashes and a two-character fan-out, and reproducing that inside the
    /// backup would mean a directory layout the restore has to agree with. The identifier is
    /// unique, flat, and already in the rows beside it.
    /// </para>
    /// </summary>
    private static string FileNameFor(EvidenceObjectRow artefact) => $"{artefact.Id:N}.bin";
}
