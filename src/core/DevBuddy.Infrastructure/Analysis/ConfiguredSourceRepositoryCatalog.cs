using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Analysis;

/// <summary>
/// The repositories the configuration already makes reachable, read from the same two places the
/// source system clients read.
/// <para>
/// With the GitHub API mode, the entries of <see cref="GitHubOptions.Repositories"/> whose key
/// names this project. With a working copy, the directories under the project's own directory
/// whose names are repository identifiers, which is the layout
/// <see cref="AnalysisOptions.RootFor(ProjectScope, SourceRepositoryId?)"/> resolves. Only names are
/// read: nothing under those directories is opened, and nothing about the server's paths is
/// returned.
/// </para>
/// </summary>
internal sealed class ConfiguredSourceRepositoryCatalog(
    IOptions<AnalysisOptions> analysis, IOptions<GitHubOptions> github) : ISourceRepositoryCatalog
{
    private readonly AnalysisOptions _analysis = Guard.NotNull(analysis, nameof(analysis)).Value;
    private readonly GitHubOptions _github = Guard.NotNull(github, nameof(github)).Value;

    public Task<IReadOnlyList<AvailableRepository>> ListAsync(ProjectScope scope, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AvailableRepository>>(
            _github.Mode == SourceSystemMode.GitHubApi ? FromGitHub(scope) : FromWorkingCopies(scope));

    private List<AvailableRepository> FromGitHub(ProjectScope scope)
    {
        string prefix = $"{scope.ProjectId.Value}/";
        List<AvailableRepository> found = [];

        foreach ((string key, string locator) in _github.Repositories)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal)
                && Guid.TryParse(key[prefix.Length..], out Guid id))
            {
                found.Add(new AvailableRepository(new SourceRepositoryId(id), locator));
            }
        }

        return found;
    }

    private List<AvailableRepository> FromWorkingCopies(ProjectScope scope)
    {
        if (!_analysis.IsAnalysable(scope))
        {
            return [];
        }

        List<AvailableRepository> found = [];

        foreach (DirectoryInfo directory in new DirectoryInfo(_analysis.RootFor(scope)).EnumerateDirectories())
        {
            // A link could point anywhere on the server. The analysers guard every path they read;
            // this list is not the place to start trusting one.
            if (directory.LinkTarget is null && Guid.TryParse(directory.Name, out Guid id))
            {
                found.Add(new AvailableRepository(new SourceRepositoryId(id), null));
            }
        }

        return found;
    }
}
