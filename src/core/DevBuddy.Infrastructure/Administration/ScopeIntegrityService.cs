using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
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
internal sealed class ScopeIntegrityService(DevBuddyDbContext db) : IScopeIntegrityReport
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
