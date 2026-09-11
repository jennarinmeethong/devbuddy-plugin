using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Workers;

/// <summary>
/// The vector index an installation has when it has none, which is every installation until
/// somebody chooses otherwise.
/// <para>
/// A null object rather than an absent registration, and for the same reason
/// <see cref="EmbeddingGateway"/> is always present even with no provider behind it: a port that
/// disappeared when unconfigured would push every caller into asking whether it had been
/// registered before asking it anything, and a use case cannot be constructed from a dependency
/// that might not exist.
/// </para>
/// <para>
/// Registered by the Application composition with <c>TryAdd</c>, so the real PostgreSQL adapter —
/// which is registered first, being an infrastructure concern — wins wherever there is one. What
/// this leaves behind is a deployment with no persistence at all still being able to build its
/// operations, which is what the tool-surface and prompt-injection suites do.
/// </para>
/// <para>
/// It answers <b>unavailable</b> rather than empty. <c>search_similar_records</c> turns that into
/// a reason the caller can read, because "nothing resembles your question" and "this installation
/// cannot answer that question" are different answers.
/// </para>
/// </summary>
public sealed class AbsentEmbeddingIndex : IEmbeddingIndex
{
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<int> UpsertAsync(
        ProjectScope scope,
        string model,
        IReadOnlyList<EmbeddedRevision> entries,
        CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<IReadOnlyList<SimilarRevision>> FindSimilarAsync(
        ProjectScope scope,
        string model,
        ReadOnlyMemory<float> query,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SimilarRevision>>([]);

    public Task<IReadOnlySet<string>> IndexedContentHashesAsync(
        ProjectScope scope, string model, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));

    public Task<int> PurgeProjectAsync(ProjectScope scope, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
