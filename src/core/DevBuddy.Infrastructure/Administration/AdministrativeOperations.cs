using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Administration;

/// <summary>
/// Export and health.
/// <para>
/// Backup and restore are not implemented here and deliberately throw rather than returning a
/// plausible-looking manifest. They need a database dump, an object-store copy, and a restore
/// drill, which is Phase 10 work; a method that quietly returned success would put a row in the
/// verification matrix that nothing backs.
/// </para>
/// </summary>
internal sealed class AdministrativeOperations : IAdministrativeOperations
{
    private readonly DevBuddyDbContext _db;
    private readonly IClock _clock;
    private readonly EvidenceStoreOptions _evidence;

    public AdministrativeOperations(
        DevBuddyDbContext db, IClock clock, IOptions<EvidenceStoreOptions> evidence)
    {
        _db = Guard.NotNull(db, nameof(db));
        _clock = Guard.NotNull(clock, nameof(clock));
        _evidence = Guard.NotNull(evidence, nameof(evidence)).Value;
    }

    /// <summary>
    /// Summarises what an export of one project would contain.
    /// <para>
    /// Scoped like every other read, because an export is the easiest place for an isolation bug
    /// to become a bulk disclosure: one missing predicate and the file holds every project in the
    /// workspace (SB-12). The manifest carries an expiry from the moment it is created, because
    /// an export is a data copy and the retention schedule applies to copies (ADR-0009).
    /// </para>
    /// </summary>
    public async Task<ExportManifest> ExportAsync(ProjectScope scope, CancellationToken cancellationToken)
    {
        int records = await _db.KnowledgeRecords
            .CountAsync(
                record => record.WorkspaceId == scope.WorkspaceId.Value
                    && record.ProjectId == scope.ProjectId.Value,
                cancellationToken);

        int evidence = await _db.EvidenceObjects
            .CountAsync(
                artefact => artefact.WorkspaceId == scope.WorkspaceId.Value
                    && artefact.ProjectId == scope.ProjectId.Value,
                cancellationToken);

        DateTimeOffset now = _clock.UtcNow;

        return new ExportManifest(
            Reference: $"export/{scope.ProjectId.Value:N}/{now.ToUnixTimeSeconds()}",
            RecordCount: records,
            EvidenceCount: evidence,
            CreatedAt: now,
            ExpiresAt: now.AddDays(30));
    }

    public Task<BackupManifest> BackupAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Backup is Phase 10. It needs a database dump, an object-store copy, and a restore "
            + "drill; returning a manifest for none of that would be worse than refusing.");

    public Task<RestoreOutcome> RestoreAsync(string backupReference, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Restore is Phase 10, and is only meaningful once Backup exists.");

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
