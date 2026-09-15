using System.Text.Json;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Tests;
using DevBuddy.McpServer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace DevBuddy.Api.Tests;

/// <summary>
/// The call that was observed to compare nothing on 2026-09-15, made the same way: the MCP tool,
/// on the AI channel, through the real host's pipeline, over a working copy on disk whose two tags
/// point at different commits. It answered with nothing but the renamed reference, because one
/// snapshot was relabelled with both names.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SnapshotComparisonTests(ApiFixture fixture) : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "devbuddy-api-snapshots-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("refs/tags/v0.1.0", "refs/tags/v0.2.0")]
    [InlineData("v0.1.0", "v0.2.0")]
    public async Task compare_snapshots_reports_the_commit_move_between_two_tags(string earlier, string later)
    {
        ProjectScope project = await fixture.CreateProjectAsync($"Snapshots {Guid.NewGuid():N}");
        await fixture.EnableAiAccessAsync(project);
        var repository = SourceRepositoryId.New();

        var git = new GitFixture(
            Path.Combine(_root, project.ProjectId.Value.ToString(), repository.Value.ToString()));

        string first = git.Commit(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["README.md"] = "# importer",
            ["src/importer.cs"] = "class Importer { }",
        });

        string second = git.Commit(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["README.md"] = "# importer",
                ["src/importer.cs"] = "class Importer { void Normalise() { } }",
                ["docs/decision.md"] = "# why",
            },
            parent: first);

        // One lightweight tag and one annotated, and HEAD on the later one, as in the observation.
        git.SetTag("v0.1.0", first);
        git.AnnotatedTag("v0.2.0", second);
        git.SetBranch("main", second);

        await using WebApplicationFactory<ApiHost> host = fixture.BuildWith(("Analysis:RootPath", _root));

        CallToolResult result = await CallToolAsync(host, project, new
        {
            scope = new { workspaceId = project.WorkspaceId.Value, projectId = project.ProjectId.Value },
            repositoryId = repository.Value,
            earlierReference = earlier,
            laterReference = later,
        });

        string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Assert.False(result.IsError, text);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(text);

        Assert.Equal("refs/tags/v0.1.0", body.GetProperty("earlier").GetProperty("reference").GetString());
        Assert.Equal(first, body.GetProperty("earlier").GetProperty("commitId").GetString());
        Assert.Equal("refs/tags/v0.2.0", body.GetProperty("later").GetProperty("reference").GetString());
        Assert.Equal(second, body.GetProperty("later").GetProperty("commitId").GetString());

        JsonElement moved = Assert.Single(body.GetProperty("differences").EnumerateArray());
        Assert.Equal("commit", moved.GetProperty("subject").GetString());
        Assert.Equal(first, moved.GetProperty("before").GetString());
        Assert.Equal(second, moved.GetProperty("after").GetString());

        string[] paths = [.. body.GetProperty("changedPaths").EnumerateArray().Select(path => path.GetString()!)];
        Assert.Equal(["docs/decision.md", "src/importer.cs"], paths);
    }

    private async Task<CallToolResult> CallToolAsync(
        WebApplicationFactory<ApiHost> host, ProjectScope project, object arguments)
    {
        using IServiceScope scope = host.Services.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        services.GetRequiredService<MutableTenantContext>().EnterWorkspace(project.WorkspaceId);

        var handlers = new McpToolHandlers(
            services.GetRequiredService<OperationDispatcher>(),
            new CallerContext(fixture.Administrator, AccessChannel.Ai, "snapshot-comparison-test"),
            new TenantEntry(
                services.GetRequiredService<MutableTenantContext>(),
                services.GetRequiredService<IWorkspaceResolver>()));

        JsonElement body = JsonSerializer.SerializeToElement(arguments, JsonConventions.Options);

        Dictionary<string, JsonElement> named = body.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);

        return await handlers.CallToolAsync(
            new CallToolRequestParams { Name = "compare_snapshots", Arguments = named }, CancellationToken.None);
    }
}
