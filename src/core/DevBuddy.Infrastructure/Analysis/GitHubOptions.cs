using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Infrastructure.Analysis;

/// <summary>Which <see cref="Application.Abstractions.ISourceSystemClient"/> implementation is active.</summary>
public enum SourceSystemMode
{
    /// <summary>Reads a mounted working copy. No network call, no token (SB-04).</summary>
    WorkingCopy = 1,

    /// <summary>Reads the GitHub REST API. Needs a token and an entry in the outbound allow-list.</summary>
    GitHubApi = 2,
}

/// <summary>
/// GitHub API access, opt-in and off by default (<see cref="SourceSystemMode.WorkingCopy"/>).
/// <para>
/// A repository is addressed the same way <see cref="AnalysisOptions"/> addresses a mounted
/// working copy: by project and repository identifier, not by a database row — nothing in this
/// system persists a <c>SourceRepository</c> today, so this mirrors the convention that already
/// exists rather than inventing a second one. <see cref="LocatorFor"/> is the lookup.
/// </para>
/// </summary>
public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public SourceSystemMode Mode { get; set; } = SourceSystemMode.WorkingCopy;

    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>
    /// A personal access token with read-only repository scope (ADR-0010). Never logged, never
    /// audited, never returned from any operation.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Maps <c>"{projectId}/{repositoryId}"</c> to the repository's <c>"owner/repo"</c> address at
    /// GitHub. One entry per repository an operator has authorised for API access.
    /// </summary>
    public IDictionary<string, string> Repositories { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Looks up the <c>owner/repo</c> address for one repository, or null if none is configured.</summary>
    public string? LocatorFor(ProjectScope scope, SourceRepositoryId repositoryId) =>
        Repositories.TryGetValue(KeyFor(scope, repositoryId), out string? locator) ? locator : null;

    private static string KeyFor(ProjectScope scope, SourceRepositoryId repositoryId) =>
        $"{scope.ProjectId.Value}/{repositoryId.Value}";
}
