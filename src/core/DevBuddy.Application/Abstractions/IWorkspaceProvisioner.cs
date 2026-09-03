using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Creates a workspace beyond the first one.
/// <para>
/// <see cref="IInstallationBootstrapper"/> creates the very first workspace on an empty database
/// and refuses forever after — there is no caller to authorise a second one against, since no
/// membership exists yet either. This port is the path for every workspace after that: an
/// existing workspace administrator sponsors a new one and becomes its administrator in turn,
/// the same shape the bootstrap uses, just running through the ordinary pipeline instead of
/// around it. There is no separate "installation administrator" concept — that would be a
/// materially different security model from everything else in this system.
/// </para>
/// </summary>
public interface IWorkspaceProvisioner
{
    Task<WorkspaceProvisioningResult> CreateAsync(
        string name, UserId administrator, string? firstProjectName, CancellationToken cancellationToken);
}

/// <summary>What creating a workspace produced.</summary>
public sealed record WorkspaceProvisioningResult(WorkspaceId WorkspaceId, ProjectId? ProjectId);
