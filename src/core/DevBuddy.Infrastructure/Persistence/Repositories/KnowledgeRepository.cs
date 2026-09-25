using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DevBuddy.Infrastructure.Persistence.Repositories;

/// <summary>
/// Records and work items, in PostgreSQL.
/// <para>
/// Every query filters on the scope it was given **and** carries the global query filter behind
/// it. The two are not redundant: the explicit filter is the contract, and the global one is what
/// catches a query somebody adds later and forgets to scope.
/// </para>
/// </summary>
internal sealed class KnowledgeRepository(DevBuddyDbContext db) : IKnowledgeRepository
{
    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));

    public async Task<KnowledgeRecord?> FindRecordAsync(
        KnowledgeRecordId id, ProjectScope scope, CancellationToken cancellationToken)
    {
        KnowledgeRecordRow? row = await RecordsIn(scope)
            .Include(record => record.Revisions)
            .Include(record => record.Approvals)
            .Include(record => record.Corrections)
            .AsNoTracking()
            .FirstOrDefaultAsync(record => record.Id == id.Value, cancellationToken);

        return row is null ? null : RowMappers.ToDomain(row);
    }

    public async Task<IReadOnlyList<KnowledgeRecord>> ListRecordsForWorkItemAsync(
        WorkItemId workItemId, ProjectScope scope, CancellationToken cancellationToken)
    {
        List<KnowledgeRecordRow> rows = await RecordsIn(scope)
            .Include(record => record.Revisions)
            .Include(record => record.Approvals)
            .Include(record => record.Corrections)
            .Where(record => record.WorkItemId == workItemId.Value)
            .OrderBy(record => record.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(RowMappers.ToDomain)];
    }

    public async Task<WorkItem?> FindWorkItemAsync(
        WorkItemId id, ProjectScope scope, CancellationToken cancellationToken)
    {
        WorkItemRow? row = await _db.WorkItems
            .Where(item => item.WorkspaceId == scope.WorkspaceId.Value
                && item.ProjectId == scope.ProjectId.Value
                && item.Id == id.Value)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : RowMappers.ToDomain(row);
    }

    public async Task<IReadOnlyList<WorkItem>> ListWorkItemsAsync(
        ProjectScope scope, CancellationToken cancellationToken)
    {
        List<WorkItemRow> rows = await _db.WorkItems
            .Where(item => item.WorkspaceId == scope.WorkspaceId.Value
                && item.ProjectId == scope.ProjectId.Value)
            .OrderBy(item => item.Key)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(RowMappers.ToDomain)];
    }

    /// <summary>
    /// Records in the project, newest first, optionally narrowed to particular statuses.
    /// <para>
    /// The status filter is translated into SQL rather than applied after loading. A review queue
    /// that fetched every record in the project and then kept four of them would get slower every
    /// week the project ran.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeRecord>> ListRecordsAsync(
        ProjectScope scope, IReadOnlyList<RecordStatus>? statuses, CancellationToken cancellationToken)
    {
        IQueryable<KnowledgeRecordRow> query = RecordsIn(scope);

        if (statuses is { Count: > 0 })
        {
            int[] wanted = [.. statuses.Select(status => (int)status)];
            query = query.Where(record => wanted.Contains(record.Status));
        }

        List<KnowledgeRecordRow> rows = await query
            .Include(record => record.Revisions)
            .Include(record => record.Approvals)
            .Include(record => record.Corrections)
            .OrderByDescending(record => record.LastUpdatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(RowMappers.ToDomain)];
    }

    /// <summary>
    /// Adds a work item, and refuses one whose key the project already uses.
    /// <para>
    /// The unique index is what decides, because two people registering the same key at the same
    /// moment would both pass any check made first. Before Phase 14 (C4) its violation escaped as
    /// an unhandled exception, so a duplicate key answered 500. Worse, the row stayed tracked, so
    /// the pipeline's audit entry for the failed attempt tried to insert it again and failed too.
    /// The row is detached before the refusal, so the refusal is what gets audited.
    /// </para>
    /// </summary>
    public async Task AddWorkItemAsync(WorkItem workItem, CancellationToken cancellationToken)
    {
        Guard.NotNull(workItem, nameof(workItem));

        WorkItemRow row = RowMappers.ToRow(workItem);
        _db.WorkItems.Add(row);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException failure) when (failure.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_work_items_workspace_id_project_id_key",
        })
        {
            _db.Entry(row).State = EntityState.Detached;

            throw new DomainValidationException(
                $"The key {workItem.Key} is already used by another work item in this project.");
        }
    }

    public async Task AddRecordAsync(KnowledgeRecord record, CancellationToken cancellationToken)
    {
        Guard.NotNull(record, nameof(record));

        _db.KnowledgeRecords.Add(RowMappers.ToRow(record));
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Writes the aggregate back.
    /// <para>
    /// Revisions, approvals, and corrections are appended, never replaced. If the incoming
    /// aggregate carries a revision number that already exists with different content, this
    /// throws rather than writing: that is control SB-24 enforced at the storage layer, where a
    /// bug in a use case cannot get past it.
    /// </para>
    /// </summary>
    public async Task UpdateRecordAsync(KnowledgeRecord record, CancellationToken cancellationToken)
    {
        Guard.NotNull(record, nameof(record));

        KnowledgeRecordRow existing = await RecordsIn(record.Scope)
            .Include(row => row.Revisions)
            .Include(row => row.Approvals)
            .Include(row => row.Corrections)
            .FirstOrDefaultAsync(row => row.Id == record.Id.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Record {record.Id} does not exist in {record.Scope} and cannot be updated.");

        KnowledgeRecordRow incoming = RowMappers.ToRow(record);

        existing.Status = incoming.Status;
        existing.LastUpdatedAt = incoming.LastUpdatedAt;
        existing.ArchivedAt = incoming.ArchivedAt;
        existing.PublishedRevisionNumber = incoming.PublishedRevisionNumber;

        foreach (RecordRevisionRow revision in incoming.Revisions)
        {
            RecordRevisionRow? stored = existing.Revisions
                .FirstOrDefault(candidate => candidate.Number == revision.Number);

            if (stored is null)
            {
                existing.Revisions.Add(revision);
                continue;
            }

            if (!string.Equals(stored.ContentHash, revision.ContentHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Revision {revision.Number} of record {record.Id} already exists with different content. "
                    + "Revisions are immutable; add a new revision instead.");
            }

            // The one change a stored revision accepts: an administrator's after-the-fact mark
            // that an AI wrote it (Phase 13, D3). Only from unmarked to marked, and only with who
            // said so, so nothing here can clear a mark or change anything else about provenance.
            if (!stored.Provenance.DescribesAiContent() && revision.Provenance.AiMarkedBy is not null)
            {
                stored.Provenance = revision.Provenance;
            }
        }

        foreach (RecordApprovalRow approval in incoming.Approvals
            .Where(approval => existing.Approvals.All(stored => stored.Sequence != approval.Sequence)))
        {
            existing.Approvals.Add(approval);
        }

        foreach (RecordCorrectionRow correction in incoming.Corrections
            .Where(correction => existing.Corrections.All(stored => stored.Sequence != correction.Sequence)))
        {
            existing.Corrections.Add(correction);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<KnowledgeRecordRow> RecordsIn(ProjectScope scope) =>
        _db.KnowledgeRecords.Where(record =>
            record.WorkspaceId == scope.WorkspaceId.Value && record.ProjectId == scope.ProjectId.Value);
}
