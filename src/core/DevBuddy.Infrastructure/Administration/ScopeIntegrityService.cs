using System.Globalization;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Infrastructure.Administration;

/// <summary>
/// The stray-scope report, in SQL rather than through the entity model.
/// <para>
/// SQL because one of the tables, <c>record_embeddings</c>, has no entity at all and exists only
/// where pgvector does, and because the question is the same for every table: is there a
/// <c>projects</c> row with this identifier <b>and</b> this workspace. The table names are this
/// constant list and nothing a caller supplies.
/// </para>
/// </summary>
internal sealed class ScopeIntegrityService(
    DevBuddyDbContext db, IProjectDirectory projects, IAuditSink audit, IClock clock) : IScopeIntegrityReport
{
    /// <summary>Every table that stores a project identifier beside its workspace, except audit.</summary>
    internal static readonly IReadOnlyList<string> Tables =
    [
        "work_items",
        "knowledge_records",
        "evidence_objects",
        "memberships",
        "project_ai_access_policies",
        "source_repositories",
        "source_snapshots",
        "deployment_environments",
        "record_embeddings",
    ];

    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));
    private readonly IProjectDirectory _projects = Guard.NotNull(projects, nameof(projects));
    private readonly IAuditSink _audit = Guard.NotNull(audit, nameof(audit));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public async Task<IReadOnlyList<StrayScopeRows>> FindAsync(CancellationToken cancellationToken)
    {
        List<StrayScopeRows> found = [];

        foreach (string table in Tables)
        {
            // record_embeddings is created only where the pgvector extension is available.
            string qualified = "public." + table;

            bool exists = await _db.Database
                .SqlQuery<bool>($"select to_regclass({qualified}) is not null as \"Value\"")
                .SingleAsync(cancellationToken);

            if (!exists)
            {
                continue;
            }

            List<Row> rows = await _db.Database
                .SqlQueryRaw<Row>(QueryFor(table))
                .ToListAsync(cancellationToken);

            found.AddRange(rows.Select(row => new StrayScopeRows(table, row.WorkspaceId, row.ProjectId, row.Rows)));
        }

        return found;
    }

    public async Task<StrayScopePurge> PurgeAsync(
        UserId actor, int expectedRows, CancellationToken cancellationToken)
    {
        IReadOnlyList<StrayScopeRows> found = await FindAsync(cancellationToken);
        int total = found.Sum(entry => entry.Rows);

        if (total == 0)
        {
            return new StrayScopePurge(
                StrayScopePurgeOutcome.NothingToPurge,
                "No row names a project that is not a live project of its workspace. Nothing was deleted.",
                []);
        }

        if (total != expectedRows)
        {
            // Deleting is confirmed against a number the operator was shown. If the answer moved
            // since, what they confirmed is not what would be deleted.
            return new StrayScopePurge(
                StrayScopePurgeOutcome.CountChanged,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The report now finds {total} row(s), not {expectedRows}. Run scope-report again and confirm the number it shows. Nothing was deleted."),
                []);
        }

        Guid[] workspaces = [.. found.Select(entry => entry.WorkspaceId).Distinct()];

        List<MembershipRow> grants = await _db.Memberships
            .AsNoTracking()
            .Where(row => row.UserId == actor.Value && row.RevokedAt == null && row.ProjectId == null)
            .ToListAsync(cancellationToken);

        bool administersEvery = await _db.Users.AnyAsync(user => user.Id == actor.Value && !user.IsDisabled, cancellationToken)
            && workspaces.All(workspace => grants.Any(grant =>
                grant.WorkspaceId == workspace
                && RolePermissions.Grants((Role)grant.Role, PermissionKind.ManageProjects)));

        if (!administersEvery)
        {
            return new StrayScopePurge(
                StrayScopePurgeOutcome.NotAnAdministrator,
                "The actor does not administer every workspace these rows belong to, so deleting them is not theirs to do. Nothing was deleted.",
                []);
        }

        DateTimeOffset now = _clock.UtcNow;

        foreach (IGrouping<(Guid WorkspaceId, Guid ProjectId), StrayScopeRows> pair in found
                     .GroupBy(entry => (entry.WorkspaceId, entry.ProjectId)))
        {
            // The same deletion delete_project performs, keyed on the workspace and the project
            // identifier together. For a pair whose project lives in another workspace, every
            // statement it runs matches only this workspace's rows, so the other tenant's project
            // and its content are not touched.
            var scope = new ProjectScope(new WorkspaceId(pair.Key.WorkspaceId), new ProjectId(pair.Key.ProjectId));
            await _projects.DeleteProjectAsync(scope, cancellationToken);

            await _audit.WriteAsync(
                AuditEvent.ForProject(
                    AuditEventId.New(), scope, actor, AuditChannel.InternalSystem,
                    AuditAction.StrayScopeRowsPurged, AuditOutcome.Succeeded,
                    $"scope-report:{pair.Key.ProjectId}", now,
                    pair.ToDictionary(
                        entry => entry.Table,
                        entry => entry.Rows.ToString(CultureInfo.InvariantCulture),
                        StringComparer.Ordinal)),
                cancellationToken);
        }

        return new StrayScopePurge(
            StrayScopePurgeOutcome.Purged,
            string.Create(CultureInfo.InvariantCulture, $"Deleted {total} row(s)."),
            found);
    }

    /// <summary>
    /// The query for one table. The name comes from <see cref="Tables"/> and nowhere else, which is
    /// what makes building it as text safe; nothing here is a value a caller supplied. A
    /// workspace-wide membership has no project and is not a stray.
    /// </summary>
    private static string QueryFor(string table)
    {
        if (!Tables.Contains(table, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(table), table, "Not a table this report examines.");
        }

        return string.Concat(
            "select t.workspace_id as \"WorkspaceId\", t.project_id as \"ProjectId\", count(*)::int as \"Rows\" ",
            "from ", table, " t ",
            "where t.project_id is not null ",
            "and not exists (select 1 from projects p where p.id = t.project_id and p.workspace_id = t.workspace_id) ",
            "group by t.workspace_id, t.project_id ",
            "order by t.workspace_id, t.project_id");
    }

    private sealed class Row
    {
        public Guid WorkspaceId { get; set; }

        public Guid ProjectId { get; set; }

        public int Rows { get; set; }
    }
}
