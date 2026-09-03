using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Mapping;

namespace DevBuddy.Infrastructure.Administration;

/// <summary>
/// Creates a workspace beyond the first. See <see cref="IWorkspaceProvisioner"/> for why this
/// runs through the ordinary pipeline rather than beside <c>InstallationBootstrapper</c>: the
/// caller already has an account and already administers somewhere, so there is a membership to
/// authorise this against, which is exactly what the one-time bootstrap never has.
/// </summary>
internal sealed class WorkspaceProvisioner(DevBuddyDbContext db, IClock clock) : IWorkspaceProvisioner
{
    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public async Task<WorkspaceProvisioningResult> CreateAsync(
        string name, UserId administrator, string? firstProjectName, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        var workspace = new Workspace(WorkspaceId.New(), name, administrator, now);

        _db.Workspaces.Add(RowMappers.ToRow(workspace));

        // Workspace-wide, because a project-scoped administrator could not create the project
        // that follows this one.
        _db.Memberships.Add(RowMappers.ToRow(Membership.ForWorkspace(
            MembershipId.New(), administrator, workspace.Id, Role.Administrator, now, administrator)));

        ProjectId? projectId = null;

        if (!string.IsNullOrWhiteSpace(firstProjectName))
        {
            var project = new Project(ProjectId.New(), workspace.Id, firstProjectName, now);
            _db.Projects.Add(RowMappers.ToRow(project));
            projectId = project.Id;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new WorkspaceProvisioningResult(workspace.Id, projectId);
    }
}
