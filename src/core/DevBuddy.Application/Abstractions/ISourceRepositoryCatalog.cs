using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Which repositories the active source system can reach for one project.
/// <para>
/// Nothing in this system persists a <c>SourceRepository</c>. A repository is addressed by
/// identifier, and an operator makes one reachable by mounting a working copy under the project's
/// directory or by naming it in the GitHub settings. This port reports what that configuration
/// already says, so a person choosing a repository to synchronise or analyse picks from a list
/// instead of typing an identifier nobody can see.
/// </para>
/// </summary>
public interface ISourceRepositoryCatalog
{
    Task<IReadOnlyList<AvailableRepository>> ListAsync(ProjectScope scope, CancellationToken cancellationToken);
}

/// <summary>
/// One reachable repository. <paramref name="Locator"/> is the provider's own address, such as
/// <c>owner/repo</c>, when there is one. A mounted working copy has none that means anything
/// outside the server, and its path on disk is deliberately not reported.
/// </summary>
public sealed record AvailableRepository(SourceRepositoryId RepositoryId, string? Locator);

/// <summary>
/// What an operations-only container answers: nothing is reachable. The infrastructure registers
/// the real catalog first wherever it is composed.
/// </summary>
public sealed class NoSourceRepositories : ISourceRepositoryCatalog
{
    public Task<IReadOnlyList<AvailableRepository>> ListAsync(ProjectScope scope, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AvailableRepository>>([]);
}
