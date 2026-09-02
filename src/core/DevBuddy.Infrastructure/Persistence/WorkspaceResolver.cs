using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Persistence;

/// <summary>
/// Works out which workspace a request belongs to.
/// <para>
/// info.md asks for the simplest local or single-workspace experience while preserving the
/// multi-project data boundaries. This is where that lives, and it is worth being precise about
/// what it does not do: single-workspace mode saves the caller from naming a workspace. It never
/// widens a project boundary, and every project-scoped check runs exactly as it would on a
/// multi-workspace install.
/// </para>
/// <para>
/// It also refuses to guess. The moment a second workspace exists, simple mode stops resolving
/// and the caller has to say which one, because a system that silently picks one would eventually
/// pick the wrong one.
/// </para>
/// </summary>
public interface IWorkspaceResolver
{
    Task<WorkspaceId?> ResolveAsync(WorkspaceId? requested, CancellationToken cancellationToken);
}

internal sealed class WorkspaceResolver : IWorkspaceResolver
{
    private readonly DevBuddyDbContext _db;
    private readonly IdentitySettings _settings;

    public WorkspaceResolver(DevBuddyDbContext db, IOptions<IdentitySettings> settings)
    {
        _db = Guard.NotNull(db, nameof(db));
        _settings = Guard.NotNull(settings, nameof(settings)).Value;
    }

    public async Task<WorkspaceId?> ResolveAsync(
        WorkspaceId? requested, CancellationToken cancellationToken)
    {
        if (requested is { } explicitWorkspace)
        {
            // An explicit request is still only a claim. Resolving it here does not authorise it;
            // the authorization service checks membership against it afterwards (SB-11).
            return explicitWorkspace;
        }

        if (!_settings.SingleWorkspaceMode)
        {
            return null;
        }

        // Two is enough to know the answer is ambiguous, and stops the query short.
        List<Guid> workspaces = await _db.Workspaces
            .AsNoTracking()
            .OrderBy(workspace => workspace.Id)
            .Select(workspace => workspace.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        return workspaces.Count == 1 ? new WorkspaceId(workspaces[0]) : null;
    }
}
