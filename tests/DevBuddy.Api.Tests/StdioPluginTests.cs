using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevBuddy.Application;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Api.Tests;

/// <summary>
/// The Phase 9 walkthrough, automated: a plugin's MCP server process, over stdio, against a real
/// database, presenting the credential a plugin configuration holds.
/// <para>
/// This is the shape a locally launched Claude or Codex plugin actually runs in — a child process
/// with two pipes and a machine token in its environment — and it is where the things that only
/// break in that shape show up. It proves the four that matter: without a token nobody is
/// anybody, with one the caller is exactly its owner, that owner reaches exactly the one
/// workspace the token was minted in, and revoking one stops it on the next call rather than at
/// the next restart.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StdioPluginTests(ApiFixture fixture)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);
    private static readonly string[] Draft = ["Draft"];

    [Fact]
    public async Task without_a_token_a_plugin_is_nobody_and_is_refused()
    {
        string answer = await CallAsync(
            token: null,
            tool: UseCaseCatalog.ListProjects.Name,
            arguments: new { workspaceId = fixture.Workspace.Value });

        Assert.Contains("Refused", answer, StringComparison.Ordinal);

        // Nobody, rather than somebody named in the environment. This is what the old
        // DEVBUDDY_ACTOR variable made impossible to guarantee.
        Assert.Contains("identity", answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task an_invented_token_is_refused_the_same_way_as_no_token_at_all()
    {
        string answer = await CallAsync(
            token: "not-a-token-anybody-issued",
            tool: UseCaseCatalog.ListProjects.Name,
            arguments: new { workspaceId = fixture.Workspace.Value });

        Assert.Contains("Refused", answer, StringComparison.Ordinal);
        Assert.Contains("identity", answer, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The walkthrough: a plugin searches, analyses what it found, and drafts a record, and the
    /// draft comes out awaiting approval rather than published.
    /// </summary>
    [Fact]
    public async Task a_plugin_can_search_and_draft_and_what_it_writes_awaits_approval()
    {
        string email = $"plugin-{Guid.NewGuid():N}@example.test";
        UserId author = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(author, Role.Contributor);

        ProjectScope project = await fixture.CreateProjectAsync($"Plugin {Guid.NewGuid():N}"[..14]);
        await fixture.EnableAiAccessAsync(project);

        WorkItemId workItem = await fixture.SeedWorkItemAsync(project, $"DEV-{Guid.NewGuid():N}"[..10]);

        string token = await fixture.IssueMachineTokenAsync(author, "a laptop");

        object scope = new
        {
            workspaceId = project.WorkspaceId.Value,
            projectId = project.ProjectId.Value,
        };

        // Search: the project is open to AI, so it answers.
        string search = await CallAsync(
            token,
            UseCaseCatalog.SearchKnowledge.Name,
            new { scope, queryText = "rollback" });

        Assert.DoesNotContain("Refused", search, StringComparison.Ordinal);

        // Draft: the one write on the surface.
        string drafted = await CallAsync(
            token,
            UseCaseCatalog.CreateDraft.Name,
            new
            {
                scope,
                workItemId = workItem.Value,
                kind = "Decision",
                title = "Rolling back a migration is itself a migration",
                body = "Written by an assistant through the plugin, awaiting a person.",
                provenance = new
                {
                    sourceKind = "AiDraft",
                    sourceLocator = "plugin-walkthrough",
                    author = "an assistant",
                    recordedAt = DateTimeOffset.UtcNow,
                },
            });

        Assert.DoesNotContain("Refused", drafted, StringComparison.Ordinal);

        // And what it produced is a draft. A person has to approve it before any reader of
        // published knowledge sees it, which is the whole point of the AI surface being writable
        // in exactly one place.
        Assert.Contains("Draft", drafted, StringComparison.Ordinal);

        using HttpClient reviewer = await fixture.SignInAsAdministratorAsync();

        JsonElement queue = await Post(
            reviewer,
            UseCaseCatalog.ListRecords.Name,
            new { scope, statuses = Draft });

        Assert.Contains(
            "Rolling back a migration is itself a migration",
            queue.GetProperty("records").EnumerateArray()
                .Select(record => record.GetProperty("title").GetString()),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task a_human_only_operation_is_not_on_the_surface_a_plugin_sees()
    {
        UserId administrator = fixture.Administrator;
        string token = await fixture.IssueMachineTokenAsync(administrator, "a laptop");

        string answer = await CallAsync(
            token,
            UseCaseCatalog.PublishRecord.Name,
            new
            {
                scope = new { workspaceId = fixture.Workspace.Value, projectId = fixture.Project.Value },
                recordId = Guid.NewGuid(),
            });

        // The same answer an operation nobody defined would get, from a workspace administrator.
        // A machine token carries its owner's permissions; it does not carry a channel.
        Assert.Contains("Not found", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task revoking_a_token_stops_it_on_the_next_call()
    {
        string email = $"revoked-plugin-{Guid.NewGuid():N}@example.test";
        UserId owner = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(owner, Role.Contributor);

        string token = await fixture.IssueMachineTokenAsync(owner, "a laptop that was lost");

        string before = await CallAsync(
            token,
            UseCaseCatalog.ListProjects.Name,
            new { workspaceId = fixture.Workspace.Value });

        Assert.DoesNotContain("Refused", before, StringComparison.Ordinal);

        using HttpClient client = await fixture.SignInAsync(email, ApiFixture.AdministratorPassword);

        JsonElement listed = await Post(
            client, UseCaseCatalog.ListMachineTokens.Name, new { workspaceId = fixture.Workspace.Value });

        Guid tokenId = listed.GetProperty("tokens").EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == "a laptop that was lost")
            .GetProperty("id")
            .GetGuid();

        await Post(
            client,
            UseCaseCatalog.RevokeMachineToken.Name,
            new { workspaceId = fixture.Workspace.Value, tokenId });

        string after = await CallAsync(
            token,
            UseCaseCatalog.ListProjects.Name,
            new { workspaceId = fixture.Workspace.Value });

        // Resolved against the database on every call, so revocation is immediate rather than
        // effective at the next process restart (SB-14).
        Assert.Contains("Refused", after, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reason machine tokens are bound to a workspace, proved in the shape it actually
    /// mattered in: one person, two workspaces, one token in the environment.
    /// <para>
    /// Before this, that single token reached both, because both memberships were real and each
    /// call named a workspace its owner genuinely belonged to. Nothing here is a stricter
    /// membership check; the membership is still fine. What changed is that the credential itself
    /// now has a ceiling, and this is that ceiling seen from outside the process.
    /// </para>
    /// </summary>
    [Fact]
    public async Task a_token_minted_in_one_workspace_is_refused_in_another_its_owner_belongs_to()
    {
        string email = $"two-workspaces-{Guid.NewGuid():N}@example.test";
        UserId owner = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(owner, Role.Contributor);

        ProjectScope elsewhere = await fixture.CreateSeparateWorkspaceAsync(owner);
        await fixture.GrantInAsync(elsewhere.WorkspaceId, owner, Role.Contributor);

        string token = await fixture.IssueMachineTokenAsync(owner, "a laptop");

        string here = await CallAsync(
            token, UseCaseCatalog.ListProjects.Name, new { workspaceId = fixture.Workspace.Value });

        Assert.DoesNotContain("Refused", here, StringComparison.Ordinal);

        string there = await CallAsync(
            token,
            UseCaseCatalog.ListProjects.Name,
            new { workspaceId = elsewhere.WorkspaceId.Value });

        Assert.Contains("Refused", there, StringComparison.Ordinal);
        Assert.Contains("credential", there, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task a_token_minted_in_the_other_workspace_is_refused_here()
    {
        string email = $"other-workspace-{Guid.NewGuid():N}@example.test";
        UserId owner = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(owner, Role.Contributor);

        ProjectScope elsewhere = await fixture.CreateSeparateWorkspaceAsync(owner);
        await fixture.GrantInAsync(elsewhere.WorkspaceId, owner, Role.Contributor);

        string token = await fixture.IssueMachineTokenAsync(
            owner, "a laptop", elsewhere.WorkspaceId);

        string there = await CallAsync(
            token,
            UseCaseCatalog.ListProjects.Name,
            new { workspaceId = elsewhere.WorkspaceId.Value });

        Assert.DoesNotContain("Refused", there, StringComparison.Ordinal);

        string here = await CallAsync(
            token, UseCaseCatalog.ListProjects.Name, new { workspaceId = fixture.Workspace.Value });

        Assert.Contains("Refused", here, StringComparison.Ordinal);
    }

    /// <summary>
    /// A token issued before tokens carried a workspace leaves the caller anonymous, exactly as a
    /// missing one does. An upgraded installation refuses these rather than adopting them into a
    /// workspace nobody chose; the replacement is minted in a minute and is the honest answer.
    /// </summary>
    [Fact]
    public async Task a_token_from_before_workspace_scoping_is_refused_like_no_token_at_all()
    {
        string email = $"legacy-plugin-{Guid.NewGuid():N}@example.test";
        UserId owner = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(owner, Role.Contributor);

        string legacy = await fixture.PlantLegacyMachineTokenAsync(owner, "from the old release");

        string answer = await CallAsync(
            legacy, UseCaseCatalog.ListProjects.Name, new { workspaceId = fixture.Workspace.Value });

        Assert.Contains("Refused", answer, StringComparison.Ordinal);
        Assert.Contains("identity", answer, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The scope is a property of the credential, not of the transport. A person signed in over
    /// HTTP holds a session token with no workspace on it, and still reaches every workspace their
    /// memberships cover — which is what the web interface has always done and must keep doing.
    /// </summary>
    [Fact]
    public async Task a_signed_in_person_over_http_is_not_narrowed_by_any_machine_token_scope()
    {
        string email = $"http-two-workspaces-{Guid.NewGuid():N}@example.test";
        UserId person = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(person, Role.Contributor);

        ProjectScope elsewhere = await fixture.CreateSeparateWorkspaceAsync(person);
        await fixture.GrantInAsync(elsewhere.WorkspaceId, person, Role.Contributor);

        // A machine token exists for one of the two. It has nothing to say about a browser
        // session belonging to the same person.
        await fixture.IssueMachineTokenAsync(person, "a laptop");

        using HttpClient client = await fixture.SignInAsync(email, ApiFixture.AdministratorPassword);

        await Post(client, UseCaseCatalog.ListProjects.Name, new { workspaceId = fixture.Workspace.Value });
        await Post(
            client, UseCaseCatalog.ListProjects.Name, new { workspaceId = elsewhere.WorkspaceId.Value });
    }

    /// <summary>
    /// Over the wire, the listing is the workspace's own. A token belonging to another one is not
    /// filtered out of the projection — it is never read — and asking to revoke it answers the
    /// same as asking to revoke one that never existed.
    /// </summary>
    [Fact]
    public async Task a_listing_over_http_never_shows_or_revokes_another_workspaces_token()
    {
        string email = $"listing-{Guid.NewGuid():N}@example.test";
        UserId owner = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(owner, Role.Contributor);

        ProjectScope elsewhere = await fixture.CreateSeparateWorkspaceAsync(owner);
        await fixture.GrantInAsync(elsewhere.WorkspaceId, owner, Role.Contributor);

        await fixture.IssueMachineTokenAsync(owner, "here");
        await fixture.IssueMachineTokenAsync(owner, "elsewhere", elsewhere.WorkspaceId);

        using HttpClient client = await fixture.SignInAsync(email, ApiFixture.AdministratorPassword);

        JsonElement here = await Post(
            client, UseCaseCatalog.ListMachineTokens.Name, new { workspaceId = fixture.Workspace.Value });

        string[] names =
        [
            .. here.GetProperty("tokens").EnumerateArray()
                .Select(token => token.GetProperty("name").GetString()!)
        ];

        Assert.Contains("here", names, StringComparer.Ordinal);
        Assert.DoesNotContain("elsewhere", names, StringComparer.Ordinal);

        JsonElement other = await Post(
            client,
            UseCaseCatalog.ListMachineTokens.Name,
            new { workspaceId = elsewhere.WorkspaceId.Value });

        Guid elsewhereId = other.GetProperty("tokens").EnumerateArray()
            .Single(token => token.GetProperty("name").GetString() == "elsewhere")
            .GetProperty("id")
            .GetGuid();

        using HttpResponseMessage refused = await client.PostAsJsonAsync(
            $"/operations/{UseCaseCatalog.RevokeMachineToken.Name}",
            new { workspaceId = fixture.Workspace.Value, tokenId = elsewhereId });

        // Not found rather than forbidden. A caller who could tell the two apart could map out
        // which credentials exist in a workspace they are asking about from the outside.
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    private static async Task<JsonElement> Post(HttpClient client, string operation, object arguments)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/operations/{operation}", arguments);

        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{operation} answered {response.StatusCode}: {body}");

        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    /// <summary>
    /// Runs one tool call through a freshly launched server process and returns the rendered text.
    /// <para>
    /// A process per call rather than one shared across the suite. It is slower and it is what a
    /// plugin does: the host starts the server when it starts, and the credential it holds is read
    /// from the environment it was given.
    /// </para>
    /// </summary>
    private async Task<string> CallAsync(string? token, string tool, object arguments)
    {
        using Process server = Launch(token);

        try
        {
            await SendAsync(server, 1, "initialize", new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "devbuddy-plugin-test", version = "1.0.0" },
            });

            await ReadResponseAsync(server, 1);
            await SendAsync(server, null, "notifications/initialized", new { });
            await SendAsync(server, 2, "tools/call", new { name = tool, arguments });

            JsonElement answer = await ReadResponseAsync(server, 2);

            return answer.GetRawText();
        }
        finally
        {
            try
            {
                if (!server.HasExited)
                {
                    server.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        }
    }

    private Process Launch(string? token)
    {
        FileInfo assembly = ServerAssembly();

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = assembly.DirectoryName!,
        };

        start.ArgumentList.Add(assembly.FullName);
        start.ArgumentList.Add("--stdio");

        // Exactly the variables the plugin configuration sets, and no others.
        start.Environment["DEVBUDDY_ConnectionStrings__DevBuddy"] = fixture.ConnectionString;
        start.Environment["DEVBUDDY_Identity__SigningKey"] = ApiFixture.SigningKey;
        start.Environment["DEVBUDDY_Evidence__Provider"] = "FileSystem";
        start.Environment["DEVBUDDY_TOKEN"] = token ?? string.Empty;

        return Process.Start(start)
            ?? throw new InvalidOperationException("The MCP server process did not start.");
    }

    private static async Task SendAsync(Process server, int? id, string method, object parameters)
    {
        var message = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters,
        };

        if (id is { } value)
        {
            message["id"] = value;
        }

        await server.StandardInput.WriteAsync(JsonSerializer.Serialize(message));
        await server.StandardInput.WriteAsync('\n');
        await server.StandardInput.FlushAsync();
    }

    private static async Task<JsonElement> ReadResponseAsync(Process server, int id)
    {
        using var timeout = new CancellationTokenSource(Patience);

        while (!timeout.IsCancellationRequested)
        {
            string? line;

            try
            {
                line = await server.StandardOutput.ReadLineAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonElement message;

            try
            {
                message = JsonSerializer.Deserialize<JsonElement>(line);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException(
                    $"The server wrote something other than JSON-RPC to stdout: {Excerpt(line)}");
            }

            if (message.TryGetProperty("id", out JsonElement identifier)
                && identifier.ValueKind == JsonValueKind.Number
                && identifier.GetInt32() == id)
            {
                return message;
            }
        }

        throw new InvalidOperationException(
            $"No response to request {id} arrived. Server error output: "
            + Excerpt(await ReadAvailableErrorAsync(server)));
    }

    private static async Task<string> ReadAvailableErrorAsync(Process server)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var text = new StringBuilder();

        try
        {
            while (await server.StandardError.ReadLineAsync(timeout.Token) is { } line)
            {
                text.AppendLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Whatever arrived is enough to say what went wrong.
        }

        return text.ToString();
    }

    private static string Excerpt(string text) => text.Length <= 2000 ? text : text[..2000] + "…";

    private static FileInfo ServerAssembly()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        string framework = here.Name;
        string configuration = here.Parent?.Name ?? "Release";

        var assembly = new FileInfo(Path.Combine(
            RepositoryRoot().FullName,
            "src", "hosts", "DevBuddy.McpServer", "bin", configuration, framework,
            "DevBuddy.McpServer.dll"));

        Assert.True(assembly.Exists, $"{assembly.FullName} has not been built.");
        return assembly;
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !directory.EnumerateFiles("DevBuddy.slnx").Any())
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
