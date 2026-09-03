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
/// Writes an actual copy of one project: its work items, records with their full history, and
/// evidence rows and bytes.
/// <para>
/// The version this replaces returned a manifest with counts and a reference that named no real
/// file — nothing existed for the retention schedule to purge, which is exactly the gap
/// <c>docs/security/release-readiness.md</c> named before this. This is the fix: the same
/// row-plus-bytes shape <see cref="BackupService"/> already uses, scoped to one project instead
/// of the whole installation, and with no restore path — an export is for taking data out, not
/// putting it back.
/// </para>
/// </summary>
internal sealed class ExportService
{
    private readonly DevBuddyDbContext _db;
    private readonly IEvidenceBlobStore _blobs;
    private readonly IClock _clock;
    private readonly ExportOptions _options;

    public ExportService(
        DevBuddyDbContext db,
        IEvidenceBlobStore blobs,
        IClock clock,
        IOptions<ExportOptions> options)
    {
        _db = Guard.NotNull(db, nameof(db));
        _blobs = Guard.NotNull(blobs, nameof(blobs));
        _clock = Guard.NotNull(clock, nameof(clock));
        _options = Guard.NotNull(options, nameof(options)).Value;
    }

    public async Task<ExportManifest> ExportAsync(ProjectScope scope, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;

        // Project first, then the moment taken: that is what lets the retention sweep walk one
        // project's exports without reading every other project's directory.
        string stamp = string.Create(
            CultureInfo.InvariantCulture,
            $"export-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}")[..40];

        string reference = $"{scope.ProjectId.Value:N}/{stamp}";
        DirectoryInfo directory = Directory.CreateDirectory(Path.Combine(Root(), reference));

        ExportArchive archive = await ReadProjectAsync(scope, now, cancellationToken);

        string rows = Path.Combine(directory.FullName, "rows.json");

        await using (FileStream file = File.Create(rows))
        {
            await JsonSerializer.SerializeAsync(file, archive, ArchiveJson.Options, cancellationToken);
        }

        await CopyEvidenceOutAsync(scope, archive, directory, cancellationToken);

        return new ExportManifest(
            reference,
            archive.KnowledgeRecords.Count,
            archive.EvidenceObjects.Count,
            now,
            now + _options.Retention);
    }

    private string Root() => Path.GetFullPath(_options.RootPath);

    private async Task<ExportArchive> ReadProjectAsync(
        ProjectScope scope, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Guid workspaceId = scope.WorkspaceId.Value;
        Guid projectId = scope.ProjectId.Value;

        return new ExportArchive
        {
            CreatedAt = now,
            WorkspaceId = workspaceId,
            ProjectId = projectId,

            WorkItems = await _db.WorkItems
                .IgnoreQueryFilters()
                .Where(row => row.WorkspaceId == workspaceId && row.ProjectId == projectId)
                .AsNoTracking()
                .ToListAsync(cancellationToken),

            KnowledgeRecords = await _db.KnowledgeRecords
                .IgnoreQueryFilters()
                .Where(row => row.WorkspaceId == workspaceId && row.ProjectId == projectId)
                .Include(row => row.Revisions)
                .Include(row => row.Approvals)
                .Include(row => row.Corrections)
                .AsNoTracking()
                .ToListAsync(cancellationToken),

            EvidenceObjects = await _db.EvidenceObjects
                .IgnoreQueryFilters()
                .Where(row => row.WorkspaceId == workspaceId && row.ProjectId == projectId)
                .AsNoTracking()
                .ToListAsync(cancellationToken),
        };
    }

    private async Task CopyEvidenceOutAsync(
        ProjectScope scope, ExportArchive archive, DirectoryInfo directory, CancellationToken cancellationToken)
    {
        if (archive.EvidenceObjects.Count == 0)
        {
            return;
        }

        DirectoryInfo evidence = directory.CreateSubdirectory("evidence");

        foreach (EvidenceObjectRow artefact in archive.EvidenceObjects)
        {
            string target = Path.Combine(evidence.FullName, $"{artefact.Id:N}.bin");

            try
            {
                await using Stream source = await _blobs.OpenReadAsync(scope, artefact.StorageKey, cancellationToken);
                await using FileStream file = File.Create(target);
                await source.CopyToAsync(file, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                // A row whose bytes are already gone. The export still names it in rows.json; the
                // rest of the export is still worth taking.
            }
        }
    }
}
