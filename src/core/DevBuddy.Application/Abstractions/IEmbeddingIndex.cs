using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// The derived vector index ADR-0012 permits: never a source of truth, droppable, rebuildable.
/// <para>
/// <b>It holds no text.</b> A row is a workspace, a project, a record, a revision number, a content
/// hash, the model that produced the vector, and the vector. Nothing in it can be read back as
/// content, which bounds what a mistake here can cost: the worst a wrongly scoped query could
/// disclose is <i>which record resembles a question</i> — real disclosure, and the reason for the
/// scope rule below, but not the record itself.
/// </para>
/// <para>
/// <b>Every method takes a <see cref="ProjectScope"/>, and the scope is a <c>where</c> clause
/// rather than a filter applied afterwards.</b> ADR-0012 requires isolation in the query, and that
/// is not stylistic: a global nearest-neighbour search followed by a post-filter would have already
/// computed, ranked, and returned another project's rows before anything checked, and the row count
/// alone leaks. There is no method here that can be called without a scope.
/// </para>
/// <para>
/// <b>Vectors from two models are not comparable</b>, so a similarity query names the model it is
/// asking about and matches on it. Comparing a 1536-dimension vector from one model against a
/// 768-dimension vector from another does not fail — it returns a confident ranking of nonsense,
/// which is the worst failure mode available.
/// </para>
/// <para>
/// The index may be <b>absent</b>: <see cref="IsAvailableAsync"/> answers false on a PostgreSQL
/// without the <c>vector</c> extension, and the migration that creates this table skips it there
/// rather than failing. A deployment with no embedding provider configured neither needs nor gets
/// any of this, and full-text search is unaffected either way.
/// </para>
/// </summary>
public interface IEmbeddingIndex
{
    /// <summary>
    /// Whether this installation's database can hold vectors — the <c>vector</c> extension is
    /// installed and the table exists.
    /// <para>
    /// A question rather than a property because the answer is in the database, and a property
    /// that lied on a server the migration skipped would turn a clear refusal into an error at
    /// the first write.
    /// </para>
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes or replaces the vectors for one project's revisions. Keyed by record and revision,
    /// so re-embedding a revision replaces its row rather than accumulating duplicates that would
    /// each rank separately.
    /// </summary>
    Task<int> UpsertAsync(
        ProjectScope scope,
        string model,
        IReadOnlyList<EmbeddedRevision> entries,
        CancellationToken cancellationToken);

    /// <summary>
    /// The nearest revisions to <paramref name="query"/> within this project and this model,
    /// closest first.
    /// </summary>
    Task<IReadOnlyList<SimilarRevision>> FindSimilarAsync(
        ProjectScope scope,
        string model,
        ReadOnlyMemory<float> query,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// What is already indexed for this project and model, as revision content hashes. Lets a
    /// rebuild embed only what changed, which matters when every call costs money.
    /// </summary>
    Task<IReadOnlySet<string>> IndexedContentHashesAsync(
        ProjectScope scope, string model, CancellationToken cancellationToken);

    /// <summary>
    /// Drops everything this project has indexed. Called when the project is deleted, and
    /// available to an operator who wants to rebuild from nothing.
    /// </summary>
    Task<int> PurgeProjectAsync(ProjectScope scope, CancellationToken cancellationToken);
}

/// <summary>
/// One revision's vector, ready to store. The content hash is the revision's own, so a later run
/// can tell an unchanged revision from one that needs re-embedding without reading its text.
/// </summary>
public sealed record EmbeddedRevision(
    KnowledgeRecordId RecordId,
    int RevisionNumber,
    string ContentHash,
    ReadOnlyMemory<float> Vector);

/// <summary>
/// A hit. <paramref name="Distance"/> is cosine distance — 0 is identical, 2 is opposite — and is
/// returned rather than converted to a similarity score, because the conversion depends on the
/// metric and a number called "score" invites comparison across models that are not comparable.
/// </summary>
public sealed record SimilarRevision(
    KnowledgeRecordId RecordId,
    int RevisionNumber,
    double Distance);
