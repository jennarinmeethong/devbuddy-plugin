using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Infrastructure.Analysis;

/// <summary>
/// The knowledge-quality sweeps: provenance, duplicates, staleness.
/// <para>
/// All three report and none of them repair. A sweep that silently rewrote provenance or merged
/// two records would destroy the traceability the system exists to provide, and it would do it
/// invisibly. A human decides what a finding means.
/// </para>
/// </summary>
internal sealed class KnowledgeQualityChecks(DevBuddyDbContext db) : IKnowledgeQualityChecks
{
    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));

    /// <summary>
    /// Records whose origin cannot be checked: no evidence cited, or a source locator that says
    /// nothing. A published record that nobody can trace back is the failure mode this system
    /// exists to prevent, so it is reported even though it is not an error.
    /// </summary>
    public async Task<IReadOnlyList<QualityFinding>> ValidateProvenanceAsync(
        ProjectScope scope, CancellationToken cancellationToken)
    {
        List<RevisionSnapshot> revisions = await CurrentRevisionsAsync(scope, cancellationToken);
        List<QualityFinding> findings = [];

        foreach (RevisionSnapshot revision in revisions)
        {
            if (revision.EvidenceCount == 0)
            {
                findings.Add(new QualityFinding(
                    new KnowledgeRecordId(revision.RecordId),
                    "no-evidence",
                    $"Revision {revision.Number} cites no evidence."));
            }

            if (string.IsNullOrWhiteSpace(revision.SourceLocator))
            {
                findings.Add(new QualityFinding(
                    new KnowledgeRecordId(revision.RecordId),
                    "no-source-locator",
                    $"Revision {revision.Number} records no way to find its origin again."));
            }

            if (revision.IsAiGenerated && revision.Status != (int)RecordStatus.Published)
            {
                findings.Add(new QualityFinding(
                    new KnowledgeRecordId(revision.RecordId),
                    "unreviewed-ai-draft",
                    $"Revision {revision.Number} came from an AI draft and has not been published."));
            }
        }

        return findings;
    }

    /// <summary>
    /// Records that say the same thing twice, found by content hash.
    /// <para>
    /// Exact matching only. A near-duplicate finder needs embeddings, and info.md defers those
    /// until a provider is separately approved; guessing with a similarity heuristic would produce
    /// findings nobody could check.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<QualityFinding>> DetectDuplicatesAsync(
        ProjectScope scope, CancellationToken cancellationToken)
    {
        List<RevisionSnapshot> revisions = await CurrentRevisionsAsync(scope, cancellationToken);

        return
        [
            .. revisions
                .GroupBy(revision => revision.ContentHash, StringComparer.Ordinal)
                .Where(group => group.Select(revision => revision.RecordId).Distinct().Count() > 1)
                .SelectMany(group => group.Select(revision => new QualityFinding(
                    new KnowledgeRecordId(revision.RecordId),
                    "duplicate-content",
                    $"Identical content to {group.Select(other => other.RecordId).Distinct().Count() - 1} "
                    + $"other record(s), hash {group.Key[..12]}.")))
        ];
    }

    /// <summary>
    /// Knowledge nobody has touched for long enough to be suspect. Flagged, never deleted: an old
    /// record that is still true is the most valuable kind, and only a human can tell which it is.
    /// </summary>
    public async Task<IReadOnlyList<QualityFinding>> DetectStalenessAsync(
        ProjectScope scope, DateTimeOffset staleBefore, CancellationToken cancellationToken)
    {
        var stale = await _db.KnowledgeRecords
            .Where(record => record.WorkspaceId == scope.WorkspaceId.Value
                && record.ProjectId == scope.ProjectId.Value
                && record.ArchivedAt == null
                && record.LastUpdatedAt < staleBefore)
            .Select(record => new { record.Id, record.Status, record.LastUpdatedAt })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return
        [
            .. stale.Select(record => new QualityFinding(
                new KnowledgeRecordId(record.Id),
                record.Status == (int)RecordStatus.Published ? "stale-published" : "stale-draft",
                $"Last updated {record.LastUpdatedAt:yyyy-MM-dd}, status {(RecordStatus)record.Status}."))
        ];
    }

    /// <summary>
    /// The revision each record is currently represented by: the published one where there is
    /// one, the newest otherwise. Checking every historical revision would report findings about
    /// text that has already been corrected.
    /// </summary>
    private async Task<List<RevisionSnapshot>> CurrentRevisionsAsync(
        ProjectScope scope, CancellationToken cancellationToken)
    {
        var rows = await (
            from record in _db.KnowledgeRecords
            join revision in _db.RecordRevisions on record.Id equals revision.RecordId
            where record.WorkspaceId == scope.WorkspaceId.Value
                && record.ProjectId == scope.ProjectId.Value
                && record.ArchivedAt == null
            select new
            {
                record.Id,
                record.Status,
                record.PublishedRevisionNumber,
                revision.Number,
                revision.ContentHash,
                revision.Provenance,
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .GroupBy(row => row.Id)
                .Select(group => group
                    .OrderByDescending(row =>
                        row.PublishedRevisionNumber == row.Number ? int.MaxValue : row.Number)
                    .First())
                .Select(row => new RevisionSnapshot(
                    row.Id,
                    row.Status,
                    row.Number,
                    row.ContentHash,
                    row.Provenance.SourceLocator,
                    row.Provenance.Evidence.Count,
                    row.Provenance.SourceKind == (int)ProvenanceSourceKind.AiDraft))
        ];
    }

    private sealed record RevisionSnapshot(
        Guid RecordId,
        int Status,
        int Number,
        string ContentHash,
        string SourceLocator,
        int EvidenceCount,
        bool IsAiGenerated);
}
