using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.UseCases.Administration;

// Export, backup, restore, and health.
//
// Backup, restore, and health are system-wide, but they still carry a workspace: the caller is
// an administrator of somewhere, the authorization service checks that, and the audit entry has
// a scope to land in. A workspace-less administrative action would be an unattributable one.

public sealed record ExportProjectRequest(ProjectScope Scope) : ProjectRequest(Scope)
{
    public override string ResourceReference => "project";
}

/// <summary>
/// Exports one project.
/// <para>
/// The manifest carries an expiry from the first version rather than gaining one later. An
/// export is a data copy, and the retention schedule (ADR-0009) applies to copies as much as to
/// the primary database, which is exactly the part that usually gets forgotten.
/// </para>
/// </summary>
public sealed class ExportProjectUseCase(IAdministrativeOperations operations)
    : UseCase<ExportProjectRequest, ExportManifest>
{
    private readonly IAdministrativeOperations _operations = Guard.NotNull(operations, nameof(operations));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ExportProject;

    protected internal override Task<ExportManifest> HandleAsync(
        ExportProjectRequest request, CallerContext caller, CancellationToken cancellationToken) =>
        _operations.ExportAsync(request.Scope, cancellationToken);
}

public sealed record AdministrativeRequest(WorkspaceId WorkspaceId) : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => "system";
}

/// <summary>Takes a backup. Restoring from one is tested each release, not assumed (SB-33).</summary>
public sealed class BackupSystemUseCase(IAdministrativeOperations operations)
    : UseCase<AdministrativeRequest, BackupManifest>
{
    private readonly IAdministrativeOperations _operations = Guard.NotNull(operations, nameof(operations));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.BackupSystem;

    protected internal override Task<BackupManifest> HandleAsync(
        AdministrativeRequest request, CallerContext caller, CancellationToken cancellationToken) =>
        _operations.BackupAsync(cancellationToken);
}

// Restore has no use case, and that is a finding rather than an omission. Every operation is
// authorised against a membership; a restore from total loss runs against a database with no
// memberships in it, so the caller does not exist yet and the pipeline correctly refuses. Restore
// is a console command, outside the pipeline, for the same structural reason as migrate and the
// one-time bootstrap. See docs/operations/backup-and-restore.md.

/// <summary>
/// Reports component health. Names components and whether they answered, never connection
/// strings or credentials: a health endpoint is one of the easiest places to leak them.
/// </summary>
public sealed class CheckSystemHealthUseCase(IAdministrativeOperations operations)
    : UseCase<AdministrativeRequest, HealthReport>
{
    private readonly IAdministrativeOperations _operations = Guard.NotNull(operations, nameof(operations));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.CheckSystemHealth;

    protected internal override Task<HealthReport> HandleAsync(
        AdministrativeRequest request, CallerContext caller, CancellationToken cancellationToken) =>
        _operations.CheckHealthAsync(cancellationToken);
}
