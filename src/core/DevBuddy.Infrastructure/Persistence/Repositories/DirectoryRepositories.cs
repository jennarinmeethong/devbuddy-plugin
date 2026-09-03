using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Infrastructure.Persistence.Repositories;

/// <summary>
/// The tenancy graph. Only ever returns the projects a named user is actually a member of, which
/// is what narrows AI results to the requesting user rather than to the AI credential (SB-09).
/// </summary>
internal sealed class ProjectDirectory(DevBuddyDbContext db, IEvidenceBlobStore blobs) : IProjectDirectory
{
    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));
    private readonly IEvidenceBlobStore _blobs = Guard.NotNull(blobs, nameof(blobs));

    public async Task<IReadOnlyList<Project>> ListProjectsForUserAsync(
        WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken)
    {
        // A workspace-wide grant covers every project; a project grant covers one. Both are
        // resolved here rather than by the caller, so there is one definition of what a user can
        // see rather than one per call site.
        List<MembershipRow> grants = await _db.Memberships
            .Where(membership => membership.WorkspaceId == workspaceId.Value
                && membership.UserId == userId.Value
                && membership.RevokedAt == null)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (grants.Count == 0)
        {
            return [];
        }

        bool coversWholeWorkspace = grants.Exists(grant => grant.ProjectId is null);

        Guid[] grantedProjects =
            [.. grants.Where(grant => grant.ProjectId is not null).Select(grant => grant.ProjectId!.Value)];

        List<ProjectRow> rows = await _db.Projects
            .Where(project => project.WorkspaceId == workspaceId.Value
                && (coversWholeWorkspace || grantedProjects.Contains(project.Id)))
            .OrderBy(project => project.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(RowMappers.ToDomain)];
    }

    public async Task<Project?> FindProjectAsync(ProjectScope scope, CancellationToken cancellationToken)
    {
        ProjectRow? row = await _db.Projects
            .Where(project => project.WorkspaceId == scope.WorkspaceId.Value && project.Id == scope.ProjectId.Value)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : RowMappers.ToDomain(row);
    }

    public async Task AddProjectAsync(Project project, CancellationToken cancellationToken)
    {
        _db.Projects.Add(RowMappers.ToRow(Guard.NotNull(project, nameof(project))));
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Evidence bytes are removed before their rows: a blob orphaned by a failure partway through
    /// is recoverable by the retention sweep (SB-27); a blob deleted after its row already forgot
    /// the storage key is not. Everything else is a set-based delete scoped to the project, with
    /// no cross-table ordering that matters — there are no foreign keys between these tables at
    /// the database level, only a shared project identifier.
    /// </summary>
    public async Task DeleteProjectAsync(ProjectScope scope, CancellationToken cancellationToken)
    {
        Guid workspaceId = scope.WorkspaceId.Value;
        Guid projectId = scope.ProjectId.Value;

        List<EvidenceObjectRow> evidence = await _db.EvidenceObjects
            .IgnoreQueryFilters()
            .Where(row => row.WorkspaceId == workspaceId && row.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        foreach (EvidenceObjectRow artefact in evidence)
        {
            await _blobs.DeleteAsync(scope, artefact.StorageKey, cancellationToken);
        }

        await _db.EvidenceObjects
            .IgnoreQueryFilters()
            .Where(row => row.WorkspaceId == workspaceId && row.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        // Revisions, approvals, and corrections cascade from this at the database level
        // (DeleteBehavior.Cascade on each foreign key), so a set-based delete here is enough.
        await _db.KnowledgeRecords
            .IgnoreQueryFilters()
            .Where(row => row.WorkspaceId == workspaceId && row.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.WorkItems
            .IgnoreQueryFilters()
            .Where(row => row.WorkspaceId == workspaceId && row.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.SourceSnapshots
            .IgnoreQueryFilters()
            .Where(row => row.WorkspaceId == workspaceId && row.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.SourceRepositories
            .IgnoreQueryFilters()
            .Where(row => row.WorkspaceId == workspaceId && row.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.DeploymentEnvironments
            .IgnoreQueryFilters()
            .Where(row => row.WorkspaceId == workspaceId && row.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.AiAccessPolicies
            .IgnoreQueryFilters()
            .Where(row => row.WorkspaceId == workspaceId && row.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        // Not filtered in the model (an administrator revoking access has to see a grant in order
        // to revoke it), so no IgnoreQueryFilters call is needed here.
        await _db.Memberships
            .Where(row => row.WorkspaceId == workspaceId && row.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.Projects
            .IgnoreQueryFilters()
            .Where(row => row.WorkspaceId == workspaceId && row.Id == projectId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>Team administration. See <see cref="ITeamDirectory"/> for what a team is and is not.</summary>
internal sealed class TeamDirectory(DevBuddyDbContext db) : ITeamDirectory
{
    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));

    public async Task<IReadOnlyList<Team>> ListTeamsAsync(
        WorkspaceId workspaceId, CancellationToken cancellationToken)
    {
        List<TeamRow> rows = await _db.Teams
            .Where(team => team.WorkspaceId == workspaceId.Value)
            .OrderBy(team => team.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(RowMappers.ToDomain)];
    }

    public async Task<Team?> FindTeamAsync(
        TeamId id, WorkspaceId workspaceId, CancellationToken cancellationToken)
    {
        TeamRow? row = await _db.Teams
            .Where(team => team.WorkspaceId == workspaceId.Value && team.Id == id.Value)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : RowMappers.ToDomain(row);
    }

    public async Task AddTeamAsync(Team team, CancellationToken cancellationToken)
    {
        _db.Teams.Add(RowMappers.ToRow(Guard.NotNull(team, nameof(team))));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateTeamAsync(Team team, CancellationToken cancellationToken)
    {
        Guard.NotNull(team, nameof(team));

        TeamRow existing = await _db.Teams
            .FirstOrDefaultAsync(row => row.Id == team.Id.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Team {team.Id} does not exist.");

        existing.Name = team.Name;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteTeamAsync(
        TeamId id, WorkspaceId workspaceId, CancellationToken cancellationToken)
    {
        await _db.TeamMembers
            .Where(member => member.WorkspaceId == workspaceId.Value && member.TeamId == id.Value)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.Teams
            .Where(team => team.WorkspaceId == workspaceId.Value && team.Id == id.Value)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserId>> ListTeamMembersAsync(
        TeamId id, CancellationToken cancellationToken)
    {
        List<Guid> ids = await _db.TeamMembers
            .AsNoTracking()
            .Where(member => member.TeamId == id.Value)
            .OrderBy(member => member.UserId)
            .Select(member => member.UserId)
            .ToListAsync(cancellationToken);

        return [.. ids.Select(value => new UserId(value))];
    }

    public async Task AddTeamMemberAsync(
        TeamId id, WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken)
    {
        bool exists = await _db.TeamMembers
            .AnyAsync(member => member.TeamId == id.Value && member.UserId == userId.Value, cancellationToken);

        if (exists)
        {
            // Adding somebody already in the team is a no-op, not a conflict: the caller asked
            // for a state, not for an event to definitely occur.
            return;
        }

        _db.TeamMembers.Add(new TeamMemberRow
        {
            TeamId = id.Value,
            WorkspaceId = workspaceId.Value,
            UserId = userId.Value,
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveTeamMemberAsync(TeamId id, UserId userId, CancellationToken cancellationToken)
    {
        await _db.TeamMembers
            .Where(member => member.TeamId == id.Value && member.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>Membership and per-project AI access.</summary>
internal sealed class AccessDirectory(DevBuddyDbContext db) : IAccessDirectory
{
    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));

    public async Task<IReadOnlyList<Membership>> ListMembershipsAsync(
        WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken)
    {
        List<MembershipRow> rows = await _db.Memberships
            .Where(membership => membership.WorkspaceId == workspaceId.Value
                && membership.UserId == userId.Value)
            .OrderBy(membership => membership.GrantedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(RowMappers.ToDomain)];
    }

    public async Task<IReadOnlyList<Membership>> ListMembershipsForWorkspaceAsync(
        WorkspaceId workspaceId, CancellationToken cancellationToken)
    {
        // Revoked grants are included rather than filtered out. An administration screen that
        // showed only live ones would make a revocation look like the grant never happened, and
        // "who used to have this" is exactly what somebody looking at the screen wants to know.
        List<MembershipRow> rows = await _db.Memberships
            .Where(membership => membership.WorkspaceId == workspaceId.Value)
            .OrderBy(membership => membership.GrantedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(RowMappers.ToDomain)];
    }

    public async Task AddMembershipAsync(Membership membership, CancellationToken cancellationToken)
    {
        _db.Memberships.Add(RowMappers.ToRow(Guard.NotNull(membership, nameof(membership))));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateMembershipAsync(Membership membership, CancellationToken cancellationToken)
    {
        Guard.NotNull(membership, nameof(membership));

        MembershipRow existing = await _db.Memberships
            .FirstOrDefaultAsync(row => row.Id == membership.Id.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Membership {membership.Id} does not exist.");

        existing.Role = (int)membership.Role;
        existing.RevokedAt = membership.RevokedAt;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Membership?> FindMembershipAsync(MembershipId id, CancellationToken cancellationToken)
    {
        MembershipRow? row = await _db.Memberships
            .AsNoTracking()
            .FirstOrDefaultAsync(membership => membership.Id == id.Value, cancellationToken);

        return row is null ? null : RowMappers.ToDomain(row);
    }

    /// <summary>
    /// Returns a disabled policy when no row exists. A project nobody configured and a project
    /// configured to deny behave identically, so forgetting to configure one cannot open it up.
    /// </summary>
    public async Task<ProjectAiAccessPolicy> GetAiAccessPolicyAsync(
        ProjectScope scope, CancellationToken cancellationToken)
    {
        ProjectAiAccessPolicyRow? row = await _db.AiAccessPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                policy => policy.WorkspaceId == scope.WorkspaceId.Value
                    && policy.ProjectId == scope.ProjectId.Value,
                cancellationToken);

        return row is null ? new ProjectAiAccessPolicy(scope) : RowMappers.ToDomain(row);
    }

    public async Task SaveAiAccessPolicyAsync(
        ProjectAiAccessPolicy policy, CancellationToken cancellationToken)
    {
        Guard.NotNull(policy, nameof(policy));

        ProjectAiAccessPolicyRow incoming = RowMappers.ToRow(policy);

        ProjectAiAccessPolicyRow? existing = await _db.AiAccessPolicies
            .FirstOrDefaultAsync(
                row => row.WorkspaceId == incoming.WorkspaceId && row.ProjectId == incoming.ProjectId,
                cancellationToken);

        if (existing is null)
        {
            _db.AiAccessPolicies.Add(incoming);
        }
        else
        {
            existing.IsEnabled = incoming.IsEnabled;
            existing.EnabledBy = incoming.EnabledBy;
            existing.EnabledAt = incoming.EnabledAt;
            existing.BoundedDataScope = incoming.BoundedDataScope;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// The audit trail. Append-only by construction: there is no update or delete on this type, and
/// the row has no method that would let a caller rewrite one.
/// </summary>
internal sealed class AuditStore(DevBuddyDbContext db) : IAuditSink, IAuditReader
{
    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));

    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        _db.AuditEvents.Add(RowMappers.ToRow(Guard.NotNull(auditEvent, nameof(auditEvent))));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(
        ProjectScope scope,
        DateTimeOffset occurredFrom,
        DateTimeOffset occurredUntil,
        UserId? actorId,
        CancellationToken cancellationToken)
    {
        IQueryable<AuditEventRow> query = _db.AuditEvents
            .Where(entry => entry.WorkspaceId == scope.WorkspaceId.Value
                && entry.ProjectId == scope.ProjectId.Value
                && entry.OccurredAt >= occurredFrom
                && entry.OccurredAt < occurredUntil);

        if (actorId is { } actor)
        {
            query = query.Where(entry => entry.ActorId == actor.Value);
        }

        List<AuditEventRow> rows = await query
            .OrderByDescending(entry => entry.OccurredAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(RowMappers.ToDomain)];
    }
}

/// <summary>
/// Evidence metadata in PostgreSQL. The bytes live in the object store; this is the record of
/// what they are and whether they have been cleared for release.
/// </summary>
internal sealed class EvidenceMetadataStore(DevBuddyDbContext db)
{
    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));

    public async Task<EvidenceObject?> FindAsync(
        EvidenceObjectId id, ProjectScope scope, CancellationToken cancellationToken)
    {
        EvidenceObjectRow? row = await Scoped(scope)
            .AsNoTracking()
            .FirstOrDefaultAsync(evidence => evidence.Id == id.Value, cancellationToken);

        return row is null ? null : RowMappers.ToDomain(row);
    }

    public async Task<IReadOnlyList<EvidenceObject>> ListForScopeAsync(
        ProjectScope scope, CancellationToken cancellationToken)
    {
        List<EvidenceObjectRow> rows = await Scoped(scope)
            .OrderByDescending(evidence => evidence.CapturedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(RowMappers.ToDomain)];
    }

    public async Task SaveAsync(EvidenceObject evidence, CancellationToken cancellationToken)
    {
        Guard.NotNull(evidence, nameof(evidence));

        EvidenceObjectRow incoming = RowMappers.ToRow(evidence);

        EvidenceObjectRow? existing = await _db.EvidenceObjects
            .FirstOrDefaultAsync(row => row.Id == incoming.Id, cancellationToken);

        if (existing is null)
        {
            _db.EvidenceObjects.Add(incoming);
        }
        else
        {
            existing.RedactionState = incoming.RedactionState;
            existing.ScannedAt = incoming.ScannedAt;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<EvidenceObjectRow> Scoped(ProjectScope scope) =>
        _db.EvidenceObjects.Where(evidence =>
            evidence.WorkspaceId == scope.WorkspaceId.Value && evidence.ProjectId == scope.ProjectId.Value);
}
