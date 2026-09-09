using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Analysis;
using Microsoft.Extensions.Configuration;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// What happens when <c>Analysis:RootPath</c> arrives blank.
/// <para>
/// Not a hypothetical. Both plugin packages name the setting in their server configuration, and a
/// configuration that expands an environment variable nobody exported delivers it present and
/// empty. A present-and-empty setting is not the same as an absent one: it overrides the default
/// rather than leaving it alone, which is the part that surprises.
/// </para>
/// </summary>
public sealed class AnalysisRootConfigurationTests
{
    private readonly ProjectScope _scope = new(WorkspaceId.New(), ProjectId.New());

    /// <summary>
    /// The premise the rest of this file rests on, asserted rather than assumed.
    /// </summary>
    [Fact]
    public void an_empty_configured_value_replaces_the_default_rather_than_leaving_it()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Analysis:RootPath"] = string.Empty,
            })
            .Build();

        AnalysisOptions options = new();

        Assert.True(options.IsConfigured);

        configuration.GetSection(AnalysisOptions.SectionName).Bind(options);

        Assert.Equal(string.Empty, options.RootPath);
        Assert.False(options.IsConfigured);
    }

    /// <summary>
    /// The regression this file exists for. <c>Path.GetFullPath</c> throws on an empty path, so
    /// asking whether a project was analysable used to end in an ArgumentException from inside a
    /// question that should only ever answer yes or no.
    /// </summary>
    [Fact]
    public void asking_whether_a_project_is_analysable_answers_rather_than_throwing()
    {
        AnalysisOptions options = new() { RootPath = string.Empty };

        Assert.False(options.IsAnalysable(_scope));
        Assert.False(options.IsAnalysable(_scope, SourceRepositoryId.New()));
    }

    /// <summary>
    /// Resolving a path is a different question from whether one exists, and it has no false to
    /// return. It says which setting is missing instead of composing a path from nothing.
    /// </summary>
    [Fact]
    public void resolving_a_path_without_a_root_names_the_setting()
    {
        AnalysisOptions options = new() { RootPath = "   " };

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(() => options.RootFor(_scope));

        Assert.Contains("Analysis:RootPath", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void a_configured_root_still_resolves_to_the_project_directory()
    {
        string root = Path.Combine(Path.GetTempPath(), "devbuddy-root-" + Guid.NewGuid().ToString("N"));

        AnalysisOptions options = new() { RootPath = root };
        var repositoryId = SourceRepositoryId.New();

        Assert.Equal(
            Path.Combine(Path.GetFullPath(root), _scope.ProjectId.Value.ToString()),
            options.RootFor(_scope));

        Assert.Equal(
            Path.Combine(
                Path.GetFullPath(root),
                _scope.ProjectId.Value.ToString(),
                repositoryId.Value.ToString()),
            options.RootFor(_scope, repositoryId));
    }
}
