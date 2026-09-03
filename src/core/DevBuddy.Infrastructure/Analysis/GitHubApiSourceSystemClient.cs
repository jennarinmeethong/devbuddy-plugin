using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Scanning;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Analysis;

/// <summary>
/// Source snapshots, change sets, and — unlike <see cref="WorkingCopySourceSystemClient"/> — pull
/// requests, issues, and review comments, read live from the GitHub REST API.
/// <para>
/// Opt-in (<see cref="GitHubOptions.Mode"/> defaults to <see cref="SourceSystemMode.WorkingCopy"/>)
/// and inert until an operator both configures a token and adds <c>api.github.com</c> to
/// <c>OutboundAccess:AllowedHosts</c> — the empty allow-list default (SB-03) is not weakened by
/// this class existing. Every request still goes through <see cref="UrlGuard"/> like any other
/// outbound call this system makes.
/// </para>
/// <para>
/// One page (100 items) per list endpoint. A v1 scope limit, not an oversight: pagination adds a
/// second failure mode — a token whose rate limit runs out mid-walk — for a feature whose whole
/// point is read-only visibility, not a complete mirror of the origin.
/// </para>
/// </summary>
internal sealed class GitHubApiSourceSystemClient : ISourceSystemClient
{
    private readonly HttpClient _http;
    private readonly UrlGuard _guard;
    private readonly GitHubOptions _options;

    public GitHubApiSourceSystemClient(HttpClient http, UrlGuard guard, IOptions<GitHubOptions> options)
    {
        _http = Guard.NotNull(http, nameof(http));
        _guard = Guard.NotNull(guard, nameof(guard));
        _options = Guard.NotNull(options, nameof(options)).Value;
    }

    public async Task<SourceSnapshot> FetchSnapshotAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken)
    {
        string locator = LocatorFor(scope, repositoryId);
        RepositoryJson repository = await GetAsync<RepositoryJson>($"repos/{locator}", cancellationToken);

        RefJson head = await GetAsync<RefJson>(
            $"repos/{locator}/git/ref/heads/{repository.DefaultBranch}", cancellationToken);

        return new SourceSnapshot(
            repositoryId,
            repository.DefaultBranch,
            head.Object.Sha,
            DateTimeOffset.UtcNow,
            [$"https://github.com/{locator}/tree/{repository.DefaultBranch}"]);
    }

    public async Task<ChangeSet> FetchChangeSetAsync(
        SourceRepositoryId repositoryId,
        ProjectScope scope,
        string commitOrRange,
        CancellationToken cancellationToken)
    {
        string locator = LocatorFor(scope, repositoryId);
        int separator = commitOrRange.IndexOf("..", StringComparison.Ordinal);

        if (separator < 0)
        {
            CommitJson commit = await GetAsync<CommitJson>($"repos/{locator}/commits/{commitOrRange}", cancellationToken);

            return new ChangeSet(
                commitOrRange,
                [.. (commit.Files ?? []).Select(file => file.Filename)],
                commit.Commit.Author.Name,
                commit.Commit.Author.Date);
        }

        string baseRef = commitOrRange[..separator];
        string headRef = commitOrRange[(separator + 2)..].TrimStart('.');

        CompareJson compare = await GetAsync<CompareJson>(
            $"repos/{locator}/compare/{baseRef}...{headRef}", cancellationToken);

        IReadOnlyList<CommitJson> commits = compare.Commits ?? [];

        (string author, DateTimeOffset occurredAt) = commits.Count > 0
            ? (commits[^1].Commit.Author.Name, commits[^1].Commit.Author.Date)
            : ("unknown", DateTimeOffset.UtcNow);

        return new ChangeSet(commitOrRange, [.. (compare.Files ?? []).Select(file => file.Filename)], author, occurredAt);
    }

    /// <summary>
    /// A metadata diff between two already-fetched snapshots, the same way
    /// <see cref="WorkingCopySourceSystemClient"/> does it — no repository locator is available
    /// here to make a further API call with, and none is needed for this comparison.
    /// </summary>
    public Task<IReadOnlyList<SnapshotDifference>> CompareAsync(
        SourceSnapshot earlier, SourceSnapshot later, CancellationToken cancellationToken)
    {
        Guard.NotNull(earlier, nameof(earlier));
        Guard.NotNull(later, nameof(later));

        List<SnapshotDifference> differences = [];

        if (!string.Equals(earlier.Reference, later.Reference, StringComparison.Ordinal))
        {
            differences.Add(new SnapshotDifference("branch", earlier.Reference, later.Reference));
        }

        if (!string.Equals(earlier.CommitId, later.CommitId, StringComparison.Ordinal))
        {
            differences.Add(new SnapshotDifference(earlier.Reference, earlier.CommitId, later.CommitId));
        }

        return Task.FromResult<IReadOnlyList<SnapshotDifference>>(differences);
    }

    public async Task<IReadOnlyList<PullRequestSummary>> FetchPullRequestsAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken)
    {
        string locator = LocatorFor(scope, repositoryId);

        IReadOnlyList<PullRequestJson> pulls = await GetAsync<IReadOnlyList<PullRequestJson>>(
            $"repos/{locator}/pulls?state=all&per_page=100", cancellationToken);

        return [.. pulls.Select(pr => new PullRequestSummary(
            pr.Number, pr.Title, pr.State, pr.User?.Login ?? "unknown", pr.Body ?? string.Empty, pr.UpdatedAt, pr.HtmlUrl))];
    }

    public async Task<IReadOnlyList<IssueSummary>> FetchIssuesAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken)
    {
        string locator = LocatorFor(scope, repositoryId);

        IReadOnlyList<IssueJson> issues = await GetAsync<IReadOnlyList<IssueJson>>(
            $"repos/{locator}/issues?state=all&per_page=100", cancellationToken);

        // The issues endpoint also returns pull requests; a real issue never carries pull_request.
        return
        [
            .. issues
                .Where(issue => issue.PullRequest is null)
                .Select(issue => new IssueSummary(
                    issue.Number, issue.Title, issue.State, issue.User?.Login ?? "unknown",
                    issue.Body ?? string.Empty, issue.UpdatedAt, issue.HtmlUrl)),
        ];
    }

    public async Task<IReadOnlyList<ReviewThreadSummary>> FetchReviewThreadsAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, int pullRequestNumber, CancellationToken cancellationToken)
    {
        string locator = LocatorFor(scope, repositoryId);

        IReadOnlyList<ReviewCommentJson> comments = await GetAsync<IReadOnlyList<ReviewCommentJson>>(
            $"repos/{locator}/pulls/{pullRequestNumber}/comments?per_page=100", cancellationToken);

        return [.. comments.Select(comment => new ReviewThreadSummary(
            pullRequestNumber, comment.User?.Login ?? "unknown", comment.Body, IsResolved: false, comment.CreatedAt))];
    }

    private string LocatorFor(ProjectScope scope, SourceRepositoryId repositoryId) =>
        _options.LocatorFor(scope, repositoryId)
        ?? throw new InvalidOperationException(
            $"No GitHub repository is configured for {repositoryId} in this project. "
                + "Add its owner/repo address to GitHub:Repositories.");

    private async Task<T> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        string url = $"{_options.ApiBaseUrl.TrimEnd('/')}/{relativePath}";

        UrlDecision decision = await _guard.InspectAsync(url, cancellationToken);

        if (!decision.IsAllowed)
        {
            throw new UnauthorizedAccessException($"GitHub API access to {url} is refused: {decision.Reason}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, decision.Target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        request.Headers.UserAgent.ParseAdd("DevBuddy");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"GitHub returned an empty response for {url}.");
    }
}

// GitHub's REST shapes, named for the field they carry rather than for the domain concept they
// become — that translation happens once, in the methods above.

internal sealed record RepositoryJson([property: JsonPropertyName("default_branch")] string DefaultBranch);

internal sealed record RefJson([property: JsonPropertyName("object")] RefObjectJson Object);

internal sealed record RefObjectJson([property: JsonPropertyName("sha")] string Sha);

internal sealed record CommitJson(
    [property: JsonPropertyName("commit")] CommitMetaJson Commit,
    [property: JsonPropertyName("files")] IReadOnlyList<CommitFileJson>? Files);

internal sealed record CommitMetaJson([property: JsonPropertyName("author")] CommitAuthorJson Author);

internal sealed record CommitAuthorJson(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("date")] DateTimeOffset Date);

internal sealed record CommitFileJson([property: JsonPropertyName("filename")] string Filename);

internal sealed record CompareJson(
    [property: JsonPropertyName("commits")] IReadOnlyList<CommitJson>? Commits,
    [property: JsonPropertyName("files")] IReadOnlyList<CommitFileJson>? Files);

internal sealed record PullRequestJson(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("user")] GitHubUserJson? User,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("html_url")] string HtmlUrl);

internal sealed record IssueJson(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("user")] GitHubUserJson? User,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("pull_request")] object? PullRequest);

internal sealed record ReviewCommentJson(
    [property: JsonPropertyName("user")] GitHubUserJson? User,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

internal sealed record GitHubUserJson([property: JsonPropertyName("login")] string Login);
