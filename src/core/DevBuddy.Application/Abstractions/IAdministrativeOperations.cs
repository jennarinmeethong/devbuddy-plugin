using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Export, backup, restore, and health. Added in Phase 2; the plan named the operations but not
/// the port. None of these reaches the MCP surface: they stay under human or internal-system
/// control, which is the whole reason they sit behind their own interface.
/// </summary>
public interface IAdministrativeOperations
{
    Task<ExportManifest> ExportAsync(ProjectScope scope, CancellationToken cancellationToken);

    Task<BackupManifest> BackupAsync(CancellationToken cancellationToken);

    Task<RestoreOutcome> RestoreAsync(string backupReference, CancellationToken cancellationToken);

    Task<HealthReport> CheckHealthAsync(CancellationToken cancellationToken);
}

/// <summary>
/// What an export produced. Exports are a data copy, so they carry an expiry from the first
/// version rather than gaining one later (retention schedule, ADR-0009).
/// </summary>
public sealed record ExportManifest(
    string Reference, int RecordCount, int EvidenceCount, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>What a backup produced.</summary>
public sealed record BackupManifest(string Reference, long SizeBytes, DateTimeOffset CreatedAt);

/// <summary>The result of a restore drill or a real restore.</summary>
public sealed record RestoreOutcome(string Reference, bool Succeeded, string Detail);

/// <summary>Component health. Names components, never connection details.</summary>
public sealed record HealthReport(bool IsHealthy, IReadOnlyList<ComponentHealth> Components);

/// <summary>One component and whether it answered.</summary>
public sealed record ComponentHealth(string Name, bool IsHealthy, string Detail);
