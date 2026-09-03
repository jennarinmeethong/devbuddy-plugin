using System.Net;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Analysis;
using DevBuddy.Infrastructure.Scanning;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// Closes the last named part of the source-synchronisation gap: "pull requests, issues, and
/// review threads are not available." They are, from this client — as long as an operator both
/// configured a token and added <c>api.github.com</c> to the outbound allow-list, which the last
/// test here proves is still required.
/// </summary>
public sealed class GitHubApiSourceSystemClientTests
{
    private static readonly ProjectScope Scope = new(WorkspaceId.New(), ProjectId.New());
    private static readonly SourceRepositoryId Repository = SourceRepositoryId.New();
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task a_snapshot_reports_the_default_branch_and_its_head_commit()
    {
        var handler = new FakeHandler();
        handler.Respond("/repos/acme/widgets", """{"default_branch":"main"}""");
        handler.Respond("/repos/acme/widgets/git/ref/heads/main", """{"object":{"sha":"abc123"}}""");

        SourceSnapshot snapshot = await Client(handler).FetchSnapshotAsync(Repository, Scope, Ct);

        Assert.Equal("main", snapshot.Reference);
        Assert.Equal("abc123", snapshot.CommitId);
        Assert.Contains("acme/widgets", Assert.Single(snapshot.Links));
    }

    [Fact]
    public async Task a_change_set_for_a_single_commit_lists_the_files_it_touched()
    {
        var handler = new FakeHandler();
        handler.Respond("/repos/acme/widgets/commits/abc123", """
            {
                "commit": { "author": { "name": "Jennarin", "date": "2026-09-01T09:00:00Z" } },
                "files": [ { "filename": "src/importer.cs" }, { "filename": "README.md" } ]
            }
            """);

        ChangeSet changeSet = await Client(handler).FetchChangeSetAsync(Repository, Scope, "abc123", Ct);

        Assert.Equal("Jennarin", changeSet.Author);
        Assert.Equal(["src/importer.cs", "README.md"], changeSet.ChangedPaths);
    }

    [Fact]
    public async Task a_change_set_for_a_range_compares_the_two_ends()
    {
        var handler = new FakeHandler();
        handler.Respond("/repos/acme/widgets/compare/v1...v2", """
            {
                "commits": [ { "commit": { "author": { "name": "Reviewer", "date": "2026-09-02T09:00:00Z" } } } ],
                "files": [ { "filename": "src/api.cs" } ]
            }
            """);

        ChangeSet changeSet = await Client(handler).FetchChangeSetAsync(Repository, Scope, "v1..v2", Ct);

        Assert.Equal("Reviewer", changeSet.Author);
        Assert.Equal(["src/api.cs"], changeSet.ChangedPaths);
    }

    [Fact]
    public async Task pull_requests_are_read_and_the_issues_endpoint_filters_out_pull_requests()
    {
        var handler = new FakeHandler();

        handler.Respond("/repos/acme/widgets/pulls?state=all&per_page=100", """
            [ { "number": 1, "title": "Add importer", "state": "open", "user": { "login": "alice" },
                "body": "does the thing", "updated_at": "2026-09-01T09:00:00Z",
                "html_url": "https://github.com/acme/widgets/pull/1" } ]
            """);

        // The issues endpoint returns pull requests too; a real issue never has pull_request.
        handler.Respond("/repos/acme/widgets/issues?state=all&per_page=100", """
            [
                { "number": 2, "title": "Bug report", "state": "open", "user": { "login": "bob" },
                  "body": "it broke", "updated_at": "2026-09-01T10:00:00Z",
                  "html_url": "https://github.com/acme/widgets/issues/2", "pull_request": null },
                { "number": 1, "title": "Add importer", "state": "open", "user": { "login": "alice" },
                  "body": "does the thing", "updated_at": "2026-09-01T09:00:00Z",
                  "html_url": "https://github.com/acme/widgets/pull/1",
                  "pull_request": { "url": "https://api.github.com/repos/acme/widgets/pulls/1" } }
            ]
            """);

        GitHubApiSourceSystemClient client = Client(handler);

        IReadOnlyList<PullRequestSummary> pullRequests = await client.FetchPullRequestsAsync(Repository, Scope, Ct);
        PullRequestSummary pr = Assert.Single(pullRequests);
        Assert.Equal("alice", pr.Author);
        Assert.Equal("open", pr.State);

        IReadOnlyList<IssueSummary> issues = await client.FetchIssuesAsync(Repository, Scope, Ct);
        IssueSummary issue = Assert.Single(issues);
        Assert.Equal(2, issue.Number);
        Assert.Equal("bob", issue.Author);
    }

    [Fact]
    public async Task review_comments_are_read_and_never_report_resolution()
    {
        var handler = new FakeHandler();
        handler.Respond("/repos/acme/widgets/pulls/1/comments?per_page=100", """
            [ { "user": { "login": "reviewer" }, "body": "please fix this",
                "created_at": "2026-09-01T09:00:00Z" } ]
            """);

        IReadOnlyList<ReviewThreadSummary> threads =
            await Client(handler).FetchReviewThreadsAsync(Repository, Scope, pullRequestNumber: 1, Ct);

        ReviewThreadSummary thread = Assert.Single(threads);
        Assert.Equal("reviewer", thread.Author);
        Assert.Equal(1, thread.PullRequestNumber);

        // REST does not expose thread resolution; only GraphQL does. Claiming otherwise would be
        // reporting a state this client cannot actually observe.
        Assert.False(thread.IsResolved);
    }

    [Fact]
    public async Task a_repository_with_no_configured_locator_is_refused()
    {
        var handler = new FakeHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Client(handler, configureRepositories: false).FetchSnapshotAsync(Repository, Scope, Ct));

        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task a_host_missing_from_the_outbound_allow_list_is_refused_before_any_request()
    {
        var handler = new FakeHandler();
        var guard = new UrlGuard(new OutboundAccessOptions(), new StubResolver());

        var client = new GitHubApiSourceSystemClient(
            new HttpClient(handler), guard, Options.Create(Configured()));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.FetchSnapshotAsync(Repository, Scope, Ct));

        // Refused before the fake handler ever saw a request — the same SB-03/SB-06 guarantee
        // every other outbound call in this system gets.
        Assert.Empty(handler.Requested);
    }

    private static GitHubApiSourceSystemClient Client(FakeHandler handler, bool configureRepositories = true)
    {
        var resolver = new StubResolver();
        resolver.Allow("api.github.com");

        var options = new OutboundAccessOptions();
        options.AllowedHosts.Add("api.github.com");

        var guard = new UrlGuard(options, resolver);

        return new GitHubApiSourceSystemClient(
            new HttpClient(handler), guard, Options.Create(Configured(configureRepositories)));
    }

    private static GitHubOptions Configured(bool configureRepositories = true)
    {
        var options = new GitHubOptions { Mode = SourceSystemMode.GitHubApi, Token = "test-token" };

        if (configureRepositories)
        {
            options.Repositories[$"{Scope.ProjectId.Value}/{Repository.Value}"] = "acme/widgets";
        }

        return options;
    }

    /// <summary>Answers by URL substring, and records every URL actually requested.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly List<(string Match, string Json)> _responses = [];

        public List<string> Requested { get; } = [];

        public void Respond(string urlContains, string json) => _responses.Add((urlContains, json));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Exact match on path and query, not a substring: a repo URL is otherwise a prefix of
            // its own sub-paths (a ref lookup, a compare), and a substring match cannot tell "is
            // this the whole path" from "is this the start of a longer one."
            string path = request.RequestUri!.PathAndQuery;
            Requested.Add(path);

            (string Match, string Json) found = _responses.Find(entry => entry.Match == path);

            if (found.Json is null)
            {
                throw new InvalidOperationException($"No fake response configured for {path}.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(found.Json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>A resolver the test controls, so DNS is not part of what is under test.</summary>
    private sealed class StubResolver : IHostResolver
    {
        private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);

        public void Allow(string host) => _allowed.Add(host);

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(
                _allowed.Contains(host) ? [IPAddress.Parse("140.82.112.6")] : []);
    }
}
