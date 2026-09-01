using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// PostgreSQL full-text search plus structured filters (ADR-0003). There is no embedding or
/// vector search in v1, and any future vector index is a derived index that can be rebuilt from
/// the database, never a source of truth.
/// </summary>
public interface ISearchIndex
{
    Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        KnowledgeSearchCriteria criteria, CancellationToken cancellationToken);

    /// <summary>Rebuilds the index for one project. Human or internal-system operation only.</summary>
    Task<int> ReindexAsync(ProjectScope scope, CancellationToken cancellationToken);
}

/// <summary>
/// A search request. Scopes are a required part of the criteria rather than an optional filter,
/// so a query that forgot to scope itself does not compile.
/// </summary>
public sealed record KnowledgeSearchCriteria(
    ProjectScope Scope,
    string QueryText,
    IReadOnlyList<RecordKind>? Kinds = null,
    IReadOnlyList<RecordStatus>? Statuses = null,
    int MaxResults = 20);

/// <summary>One result: enough to decide whether to open the record, and nothing more.</summary>
public sealed record KnowledgeSearchHit(
    KnowledgeRecordId RecordId,
    RecordKind Kind,
    RecordStatus Status,
    string Title,
    string Snippet,
    double Rank);
