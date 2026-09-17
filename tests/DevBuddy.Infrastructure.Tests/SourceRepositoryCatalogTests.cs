using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Analysis;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// What a person is offered when choosing a repository. Only what the configuration already makes
/// reachable, for this project and no other.
/// </summary>
public sealed class SourceRepositoryCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devbuddy-catalog-{Guid.NewGuid():N}");

    private static readonly ProjectScope Scope =
        new(new WorkspaceId(Guid.NewGuid()), new ProjectId(Guid.NewGuid()));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task a_working_copy_offers_the_repository_directories_under_its_project()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        string project = Path.Combine(_root, Scope.ProjectId.Value.ToString());

        Directory.CreateDirectory(Path.Combine(project, first.ToString()));
        Directory.CreateDirectory(Path.Combine(project, second.ToString()));

        // Not repositories: a directory that is not named by an identifier, a file that is, and a
        // repository under somebody else's project.
        Directory.CreateDirectory(Path.Combine(project, "docs"));
        await File.WriteAllTextAsync(Path.Combine(project, Guid.NewGuid().ToString()), "not a directory");
        Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));

        IReadOnlyList<AvailableRepository> found = await Catalog(new GitHubOptions()).ListAsync(Scope, CancellationToken.None);

        Assert.Equal(
            new[] { first, second }.Order(),
            found.Select(repository => repository.RepositoryId.Value).Order());

        // Nothing about the server's disk leaves.
        Assert.All(found, repository => Assert.Null(repository.Locator));
    }

    [Fact]
    public async Task a_project_with_no_directory_offers_nothing()
    {
        Assert.Empty(await Catalog(new GitHubOptions()).ListAsync(Scope, CancellationToken.None));
    }

    [Fact]
    public async Task the_github_mode_offers_the_configured_repositories_of_this_project_only()
    {
        Guid mine = Guid.NewGuid();
        GitHubOptions github = new() { Mode = SourceSystemMode.GitHubApi };
        github.Repositories[$"{Scope.ProjectId.Value}/{mine}"] = "acme/importer";
        github.Repositories[$"{Guid.NewGuid()}/{Guid.NewGuid()}"] = "acme/somebody-else";
        github.Repositories[$"{Scope.ProjectId.Value}/not-an-identifier"] = "acme/typo";

        // A working copy on disk is not consulted in this mode.
        Directory.CreateDirectory(Path.Combine(_root, Scope.ProjectId.Value.ToString(), Guid.NewGuid().ToString()));

        AvailableRepository found = Assert.Single(await Catalog(github).ListAsync(Scope, CancellationToken.None));

        Assert.Equal(mine, found.RepositoryId.Value);
        Assert.Equal("acme/importer", found.Locator);
    }

    private ConfiguredSourceRepositoryCatalog Catalog(GitHubOptions github) =>
        new(Options.Create(new AnalysisOptions { RootPath = _root }), Options.Create(github));
}
