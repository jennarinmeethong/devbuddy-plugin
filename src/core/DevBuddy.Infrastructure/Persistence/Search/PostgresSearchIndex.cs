using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Infrastructure.Persistence.Search;

/// <summary>
/// PostgreSQL full-text search over record revisions, combined with structured filters.
/// <para>
/// No embeddings here, and that is still true of this class: v1 shipped full-text search alone
/// (ADR-0003), and it remains complete on its own. The trade-off is real — this misses a semantic
/// match an embedding would find.
/// </para>
/// <para>
/// Since Phase 12C there is a second retrieval path for installations that configure one:
/// <c>PostgresEmbeddingIndex</c>, the derived vector index ADR-0012 permits. It is a separate
/// class on purpose. This one is always present and needs nothing; that one needs the pgvector
/// extension, a configured provider, and a scope on every query. Nothing has been folded into
/// this class, so an installation with no provider runs exactly the code it always did.
/// </para>
/// </summary>
internal sealed class PostgresSearchIndex(DevBuddyDbContext db) : ISearchIndex
{
    /// <summary>
    /// How much the database is allowed to return before the published-revision selection below
    /// narrows it. Bounded so a broad query cannot pull an unbounded result set into memory.
    /// </summary>
    private const int FetchMultiplier = 4;

    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));

    public async Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        KnowledgeSearchCriteria criteria, CancellationToken cancellationToken)
    {
        Guard.NotNull(criteria, nameof(criteria));

        ProjectScope scope = criteria.Scope;

        IQueryable<KnowledgeRecordRow> records = _db.KnowledgeRecords.Where(record =>
            record.WorkspaceId == scope.WorkspaceId.Value && record.ProjectId == scope.ProjectId.Value);

        if (criteria.Kinds is { Count: > 0 })
        {
            int[] kinds = [.. criteria.Kinds.Select(kind => (int)kind)];
            records = records.Where(record => kinds.Contains(record.Kind));
        }

        if (criteria.Statuses is { Count: > 0 })
        {
            int[] statuses = [.. criteria.Statuses.Select(status => (int)status)];
            records = records.Where(record => statuses.Contains(record.Status));
        }

        List<RawHit> raw = ThaiText.IsPresentIn(criteria.QueryText)
            ? await SearchBySubstringAsync(records, criteria, cancellationToken)
            : await SearchByTextVectorAsync(records, criteria, cancellationToken);

        // One hit per record. A published record is represented by its published revision; an
        // unpublished one by its latest, because that is the revision a reviewer would open.
        return
        [
            .. raw
                .GroupBy(hit => hit.RecordId)
                .Select(group => group.OrderByDescending(hit => hit.RevisionNumber).First())
                .OrderByDescending(hit => hit.Rank)
                .Take(criteria.MaxResults)
                .Select(hit => new KnowledgeSearchHit(
                    new KnowledgeRecordId(hit.RecordId),
                    (RecordKind)hit.Kind,
                    (RecordStatus)hit.Status,
                    hit.Title,
                    Snippet(hit.Body),
                    hit.Rank))
        ];
    }

    private async Task<List<RawHit>> SearchByTextVectorAsync(
        IQueryable<KnowledgeRecordRow> records, KnowledgeSearchCriteria criteria, CancellationToken cancellationToken)
    {
        // Ordering happens before the projection on purpose: EF cannot translate an OrderBy over
        // a member of a constructed object, so the rank expression has to be the sort key itself.
        var query =
            from record in records
            join revision in _db.RecordRevisions on record.Id equals revision.RecordId
            where record.PublishedRevisionNumber == null
                || record.PublishedRevisionNumber == revision.Number
            where revision.SearchVector!.Matches(EF.Functions.PlainToTsQuery("english", criteria.QueryText))
            orderby revision.SearchVector!.Rank(EF.Functions.PlainToTsQuery("english", criteria.QueryText)) descending
            select new RawHit(
                record.Id,
                record.Kind,
                record.Status,
                record.PublishedRevisionNumber,
                revision.Number,
                revision.Title,
                revision.Body,
                revision.SearchVector!.Rank(EF.Functions.PlainToTsQuery("english", criteria.QueryText)));

        return await query
            .Take(criteria.MaxResults * FetchMultiplier)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// A query holding Thai is matched by substring instead of by the text vector.
    /// <para>
    /// The vector is built with the <c>english</c> configuration, which splits words at spaces.
    /// Thai is written without spaces between words, so a whole Thai phrase becomes one token and a
    /// word inside it could never be found. Here every space-separated term of the query must
    /// occur in the title or the body. There is no index behind it, so the scan is bounded by the
    /// project scope and the fetch limit, and English stemming does not apply to a mixed query.
    /// </para>
    /// <para>
    /// Rank is computed after the fetch: a term in the title counts twice what one in the body
    /// does, so the rank is above zero for every hit and at most one.
    /// </para>
    /// </summary>
    private async Task<List<RawHit>> SearchBySubstringAsync(
        IQueryable<KnowledgeRecordRow> records, KnowledgeSearchCriteria criteria, CancellationToken cancellationToken)
    {
        string[] terms = ThaiText.Terms(criteria.QueryText);

        var candidates =
            from record in records
            join revision in _db.RecordRevisions on record.Id equals revision.RecordId
            where record.PublishedRevisionNumber == null
                || record.PublishedRevisionNumber == revision.Number
            select new { record, revision };

        foreach (string term in terms)
        {
            string pattern = ThaiText.ContainsPattern(term);
            candidates = candidates.Where(candidate =>
                EF.Functions.ILike(candidate.revision.Title, pattern, ThaiText.Escape)
                || EF.Functions.ILike(candidate.revision.Body, pattern, ThaiText.Escape));
        }

        List<RawHit> raw = await candidates
            .OrderByDescending(candidate => candidate.record.LastUpdatedAt)
            .Take(criteria.MaxResults * FetchMultiplier)
            .Select(candidate => new RawHit(
                candidate.record.Id,
                candidate.record.Kind,
                candidate.record.Status,
                candidate.record.PublishedRevisionNumber,
                candidate.revision.Number,
                candidate.revision.Title,
                candidate.revision.Body,
                0f))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. raw.Select(hit => hit with { Rank = ThaiText.Rank(terms, hit.Title, hit.Body) })];
    }

    /// <summary>
    /// Reports how many revisions the index covers for a project.
    /// <para>
    /// There is nothing to rebuild: the search vector is a stored generated column and PostgreSQL
    /// maintains it and its GIN index on write. The operation exists because info.md requires a
    /// reindex capability, and because a later derived index would need one for real.
    /// </para>
    /// </summary>
    public Task<int> ReindexAsync(ProjectScope scope, CancellationToken cancellationToken) =>
        (from record in _db.KnowledgeRecords
         join revision in _db.RecordRevisions on record.Id equals revision.RecordId
         where record.WorkspaceId == scope.WorkspaceId.Value && record.ProjectId == scope.ProjectId.Value
         select revision.Number)
        .CountAsync(cancellationToken);

    /// <summary>
    /// A fixed-length opening of the body. Deliberately not a highlighted extract: ts_headline
    /// would need raw SQL, and a snippet is only there to help a reader decide whether to open
    /// the record.
    /// </summary>
    private static string Snippet(string body)
    {
        const int MaxLength = 240;

        string collapsed = body.ReplaceLineEndings(" ").Trim();

        return collapsed.Length <= MaxLength
            ? collapsed
            : string.Concat(collapsed.AsSpan(0, MaxLength), "...");
    }

    private sealed record RawHit(
        Guid RecordId,
        int Kind,
        int Status,
        int? PublishedRevisionNumber,
        int RevisionNumber,
        string Title,
        string Body,
        float Rank);
}
