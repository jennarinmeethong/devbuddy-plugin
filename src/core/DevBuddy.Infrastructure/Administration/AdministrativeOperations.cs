using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Administration;

/// <summary>Export, backup, restore, and health.</summary>
internal sealed class AdministrativeOperations : IAdministrativeOperations
{
    private readonly DevBuddyDbContext _db;
    private readonly EvidenceStoreOptions _evidence;
    private readonly BackupService _backups;
    private readonly ExportService _exports;

    public AdministrativeOperations(
        DevBuddyDbContext db,
        IOptions<EvidenceStoreOptions> evidence,
        BackupService backups,
        ExportService exports)
    {
        _db = Guard.NotNull(db, nameof(db));
        _evidence = Guard.NotNull(evidence, nameof(evidence)).Value;
        _backups = Guard.NotNull(backups, nameof(backups));
        _exports = Guard.NotNull(exports, nameof(exports));
    }

    /// <summary>
    /// Writes an actual copy of one project. See <see cref="ExportService"/>.
    /// <para>
    /// Scoped like every other read, because an export is the easiest place for an isolation bug
    /// to become a bulk disclosure: one missing predicate and the file holds every project in the
    /// workspace (SB-12). The manifest carries an expiry from the moment it is created, because
    /// an export is a data copy and the retention schedule applies to copies (ADR-0009).
    /// </para>
    /// </summary>
    public Task<ExportManifest> ExportAsync(ProjectScope scope, CancellationToken cancellationToken) =>
        _exports.ExportAsync(scope, cancellationToken);

    /// <summary>Rows and artefacts together. A backup of only the database is not a backup.</summary>
    public Task<BackupManifest> BackupAsync(CancellationToken cancellationToken) =>
        _backups.BackupAsync(cancellationToken);

    /// <summary>Into an empty installation, or refused. See <see cref="BackupService"/>.</summary>
    public Task<RestoreOutcome> RestoreAsync(string backupReference, CancellationToken cancellationToken) =>
        _backups.RestoreAsync(backupReference, cancellationToken);

    /// <summary>
    /// Reports which components answered. Names them and nothing else: a health endpoint is one
    /// of the easiest places to leak a connection string.
    /// </summary>
    public async Task<HealthReport> CheckHealthAsync(CancellationToken cancellationToken)
    {
        List<ComponentHealth> components = [];

        try
        {
            bool reachable = await _db.Database.CanConnectAsync(cancellationToken);
            components.Add(new ComponentHealth("database", reachable, reachable ? "reachable" : "unreachable"));
        }
        catch (InvalidOperationException)
        {
            components.Add(new ComponentHealth("database", false, "unreachable"));
        }

        components.Add(new ComponentHealth(
            "evidence-store",
            true,
            _evidence.Provider == EvidenceStoreProvider.ObjectStorage ? "object storage" : "filesystem"));

        return new HealthReport(components.TrueForAll(component => component.IsHealthy), components);
    }
}
