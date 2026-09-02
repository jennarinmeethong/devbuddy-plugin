using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Identity;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Administration;

/// <summary>What a fresh installation needs before anybody can sign in.</summary>
public sealed record BootstrapRequest(
    string WorkspaceName,
    string AdministratorEmail,
    string AdministratorPassword,
    string? ProjectName = null);

/// <summary>What the bootstrap created, or why it did nothing.</summary>
public sealed record BootstrapResult(
    bool Created,
    WorkspaceId? WorkspaceId,
    ProjectId? ProjectId,
    UserId? AdministratorId,
    string Reason);

/// <summary>
/// Creates the first workspace and the first administrator.
/// <para>
/// This is the one operation that cannot go through the pipeline, and the reason is not
/// convenience: the pipeline authorises a caller against a membership, and on an empty database
/// there is no membership for anyone to hold. Something has to make the first one. Keeping it
/// here, named, and refusing to run twice is better than a hidden path inside the API that
/// nobody remembers is there.
/// </para>
/// <para>
/// It refuses once any workspace exists. A bootstrap that could run on a populated system would
/// be a way to mint an administrator on somebody else's installation.
/// </para>
/// </summary>
public interface IInstallationBootstrapper
{
    Task<BootstrapResult> BootstrapAsync(BootstrapRequest request, CancellationToken cancellationToken);
}

internal sealed class InstallationBootstrapper(
    DevBuddyDbContext db,
    MutableTenantContext tenant,
    ICredentialManager credentials,
    IAuditSink audit,
    IClock clock,
    IOptions<IdentitySettings> settings)
    : IInstallationBootstrapper
{
    public async Task<BootstrapResult> BootstrapAsync(
        BootstrapRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        int minimum = settings.Value.MinimumPasswordLength;

        string password = request.AdministratorPassword ?? string.Empty;

        if (password.Length < minimum)
        {
            return new BootstrapResult(
                false, null, null, null,
                $"The administrator password must be at least {minimum} characters.");
        }

        if (await db.Workspaces.AsNoTracking().AnyAsync(cancellationToken))
        {
            // Not an error worth a stack trace: running `bootstrap` twice is a normal mistake,
            // and the second run doing nothing is the whole point.
            return new BootstrapResult(
                false, null, null, null,
                "This installation already has a workspace. Bootstrap runs once, on an empty database.");
        }

        if (await db.Users.AsNoTracking()
            .AnyAsync(user => user.Email == request.AdministratorEmail, cancellationToken))
        {
            return new BootstrapResult(
                false, null, null, null, "An account already exists for that email address.");
        }

        DateTimeOffset now = clock.UtcNow;
        var workspaceId = WorkspaceId.New();
        var administratorId = UserId.New();

        var user = new User(
            administratorId, request.AdministratorEmail, request.AdministratorEmail, now);

        var workspace = new Workspace(workspaceId, request.WorkspaceName, administratorId, now);

        db.Users.Add(RowMappers.ToRow(user));
        db.Workspaces.Add(RowMappers.ToRow(workspace));

        ProjectId? projectId = null;

        if (!string.IsNullOrWhiteSpace(request.ProjectName))
        {
            projectId = ProjectId.New();
            db.Projects.Add(RowMappers.ToRow(
                new Project(projectId.Value, workspaceId, request.ProjectName, now)));
        }

        // Workspace-wide, because a project-scoped administrator could not create the next
        // project. Granted by the administrator themselves, which the audit entry records.
        db.Memberships.Add(RowMappers.ToRow(Membership.ForWorkspace(
            MembershipId.New(), administratorId, workspaceId, Role.Administrator, now, administratorId)));

        // The tenant has to be entered before saving so the project and membership rows are
        // written under the workspace the filters will later read them back through.
        tenant.EnterWorkspace(workspaceId);
        await db.SaveChangesAsync(cancellationToken);

        await credentials.SetPasswordAsync(administratorId, password, cancellationToken);

        await audit.WriteAsync(
            AuditEvent.ForWorkspace(
                AuditEventId.New(), workspaceId, administratorId, AuditAction.MembershipGranted,
                AuditOutcome.Succeeded, $"workspace:{workspaceId.Value}", now,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = "installation bootstrap",
                    ["role"] = Role.Administrator.ToString(),
                }),
            cancellationToken);

        return new BootstrapResult(
            true, workspaceId, projectId, administratorId, "The installation was bootstrapped.");
    }
}
