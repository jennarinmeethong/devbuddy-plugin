using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

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
