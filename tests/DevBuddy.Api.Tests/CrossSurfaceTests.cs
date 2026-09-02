using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevBuddy.Application;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Hosting;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.McpServer;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Role = DevBuddy.Domain.Access.Role;

namespace DevBuddy.Api.Tests;

/// <summary>
/// The second Phase 7 exit criterion: a project with AI access disabled returns nothing through
/// MCP while the same query, through the API, as an authorised human, succeeds.
/// <para>
/// Both halves matter. Proving the AI channel returns nothing is easy and almost worthless on its
/// own — a server that was simply broken would pass it. The test is only evidence because the
/// identical query, over the same data, in the same container, succeeds on the human channel, and
/// because enabling the policy afterwards makes the AI channel answer too. What separates the two
/// is the policy and nothing else.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrossSurfaceTests(ApiFixture fixture)
{
    [Fact]
    public async Task ai_access_off_returns_nothing_to_mcp_while_a_human_reads_the_same_record()
    {
        string email = $"reader-{Guid.NewGuid():N}@example.test";
        UserId reader = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(reader, Role.Contributor);

        // A project of its own. The subject here is the default, and a shared project would make
        // this test depend on whether some other test had switched AI access on first.
        ProjectScope closed = await fixture.CreateProjectAsync($"Closed {Guid.NewGuid():N}"[..14]);

        Guid recordId = await PublishRecordAsync("Rollback is a migration, not a restore", closed);

        object scope = new
        {
            workspaceId = closed.WorkspaceId.Value,
            projectId = closed.ProjectId.Value,
        };

        // The human channel, over real HTTP, with a real bearer token.
        using HttpClient human = await fixture.SignInAsync(email, ApiFixture.AdministratorPassword);

        using HttpResponseMessage humanResponse = await human.PostAsJsonAsync(
            $"/operations/{UseCaseCatalog.GetRecord.Name}", new { scope, recordId });

        Assert.Equal(HttpStatusCode.OK, humanResponse.StatusCode);

        JsonElement record = await humanResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(recordId, record.GetProperty("recordId").GetGuid());

        // The AI channel, same person, same record, AI access never enabled for this project.
        CallToolResult denied = await CallToolAsync(
            reader, UseCaseCatalog.GetRecord.Name, new { scope, recordId });

        Assert.True(denied.IsError);
        Assert.DoesNotContain(
            "Rollback is a migration",
            Rendered(denied),
            StringComparison.Ordinal);

        // And now the control that separated them. Turning the policy on makes the same call
        // succeed, which is what proves the refusal above was the policy rather than a broken
        // wiring somewhere in the MCP path.
        await fixture.EnableAiAccessAsync(closed);

        CallToolResult allowed = await CallToolAsync(
            reader, UseCaseCatalog.GetRecord.Name, new { scope, recordId });

        Assert.False(allowed.IsError, Rendered(allowed));
        Assert.Contains("Rollback is a migration", Rendered(allowed), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_human_only_operation_is_refused_on_the_ai_channel_even_with_ai_access_on()
    {
        string email = $"publisher-{Guid.NewGuid():N}@example.test";
        UserId publisher = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(publisher, Role.Administrator);

        await fixture.EnableAiAccessAsync(fixture.Scope);

        CallToolResult result = await CallToolAsync(
            publisher,
            UseCaseCatalog.PublishRecord.Name,
            new
            {
                scope = new { workspaceId = fixture.Workspace.Value, projectId = fixture.Project.Value },
                recordId = Guid.NewGuid(),
            });

        // Absent from the tool list, and answered as unknown when named anyway. A workspace
        // administrator on the AI channel is still on the AI channel (SB-08).
        Assert.True(result.IsError);
        Assert.Contains("Not found", Rendered(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// Control SB-09. AI access is a project-level opt-in, not a bypass: once it is on, what any
    /// one caller gets back is still narrowed to that caller's own permissions.
    /// </summary>
    [Fact]
    public async Task with_ai_access_on_two_callers_get_different_answers_through_mcp()
    {
        UserId member = await fixture.CreateUserAsync($"member-{Guid.NewGuid():N}@example.test");
        await fixture.GrantAsync(member, Role.Contributor);

        // An account with no membership of this workspace at all. Same tool, same arguments.
        UserId outsider = await fixture.CreateUserAsync($"outsider-{Guid.NewGuid():N}@example.test");

        Guid recordId = await PublishRecordAsync("Two callers, one record");
        await fixture.EnableAiAccessAsync(fixture.Scope);

        object arguments = new
        {
            scope = new { workspaceId = fixture.Workspace.Value, projectId = fixture.Project.Value },
            recordId,
        };

        CallToolResult forMember = await CallToolAsync(member, UseCaseCatalog.GetRecord.Name, arguments);
        CallToolResult forOutsider = await CallToolAsync(outsider, UseCaseCatalog.GetRecord.Name, arguments);

        Assert.False(forMember.IsError, Rendered(forMember));
        Assert.Contains("Two callers, one record", Rendered(forMember), StringComparison.Ordinal);

        Assert.True(forOutsider.IsError);
        Assert.DoesNotContain("Two callers, one record", Rendered(forOutsider), StringComparison.Ordinal);
    }

    /// <summary>
    /// Control SB-26 on the AI channel: an unapproved revision written on top of a published
    /// record is not what a tool call returns.
    /// <para>
    /// The API half is covered in <c>OperationEndpointTests</c>. This is the surface that matters
    /// most for it — a model reading unapproved text and treating it as knowledge is the failure
    /// the whole human-gated lifecycle exists to prevent.
    /// </para>
    /// </summary>
    [Fact]
    public async Task an_unapproved_revision_is_not_served_to_the_ai_channel_either()
    {
        UserId reader = await fixture.CreateUserAsync($"ai-reader-{Guid.NewGuid():N}@example.test");
        await fixture.GrantAsync(reader, Role.Contributor);

        Guid recordId = await PublishRecordAsync("Approved text a reader is entitled to");
        await fixture.EnableAiAccessAsync(fixture.Scope);

        object scope = new { workspaceId = fixture.Workspace.Value, projectId = fixture.Project.Value };

        using HttpClient client = await fixture.SignInAsAdministratorAsync();

        await PostAsync(
            client,
            UseCaseCatalog.ReviseDraft.Name,
            new
            {
                scope,
                recordId,
                title = "Approved text a reader is entitled to",
                body = "UNAPPROVED text nobody has reviewed.",
                provenance = new
                {
                    sourceKind = nameof(ProvenanceSourceKind.HumanAuthored),
                    sourceLocator = "meeting/2026-09-03",
                    author = "integration test",
                    recordedAt = DateTimeOffset.UtcNow,
                },
            });

        CallToolResult result = await CallToolAsync(
            reader, UseCaseCatalog.GetRecord.Name, new { scope, recordId });

        Assert.False(result.IsError, Rendered(result));
        Assert.Contains("Approved text a reader is entitled to", Rendered(result), StringComparison.Ordinal);
        Assert.DoesNotContain("UNAPPROVED", Rendered(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// Drives one record from draft to published over HTTP as the administrator, and returns its
    /// identifier.
    /// </summary>
    private async Task<Guid> PublishRecordAsync(string title, ProjectScope? into = null)
    {
        ProjectScope target = into ?? fixture.Scope;

        WorkItemId workItem = await fixture.SeedWorkItemAsync(target, $"CRQ-{Guid.NewGuid():N}"[..12]);

        using HttpClient client = await fixture.SignInAsAdministratorAsync();

        object scope = new
        {
            workspaceId = target.WorkspaceId.Value,
            projectId = target.ProjectId.Value,
        };

        JsonElement draft = await PostAsync(
            client,
            UseCaseCatalog.CreateDraft.Name,
            new
            {
                scope,
                workItemId = workItem.Value,
                kind = nameof(RecordKind.Decision),
                title,
                body = $"{title}. Recorded so the next person does not have to work it out again.",
                provenance = new
                {
                    sourceKind = nameof(ProvenanceSourceKind.HumanAuthored),
                    sourceLocator = "meeting/2026-09-01",
                    author = "integration test",
                    recordedAt = DateTimeOffset.UtcNow,
                },
            });

        Guid recordId = draft.GetProperty("recordId").GetGuid();

        await PostAsync(client, UseCaseCatalog.SubmitForApproval.Name, new { scope, recordId });

        JsonElement history = await PostAsync(
            client, UseCaseCatalog.ViewRecordHistory.Name, new { scope, recordId });

        string hash = history.GetProperty("revisions").EnumerateArray()
            .OrderByDescending(revision => revision.GetProperty("number").GetInt32())
            .First()
            .GetProperty("contentHash")
            .GetString()!;

        await PostAsync(
            client,
            UseCaseCatalog.ApproveRecord.Name,
            new { scope, recordId, approvedContentHash = hash });

        await PostAsync(client, UseCaseCatalog.PublishRecord.Name, new { scope, recordId });

        return recordId;
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string operation, object arguments)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/operations/{operation}", arguments);

        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{operation} answered {response.StatusCode}: {body}");

        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    /// <summary>
    /// Calls one tool through the real MCP handlers, in a scope of the same container the API is
    /// using, on the AI channel.
    /// </summary>
    private async Task<CallToolResult> CallToolAsync(UserId actor, string tool, object arguments)
    {
        using IServiceScope scope = fixture.OpenScope(fixture.Workspace);
        IServiceProvider services = scope.ServiceProvider;

        var handlers = new McpToolHandlers(
            ApiFixture.DispatcherFor(scope),
            new CallerContext(actor, AccessChannel.Ai, "cross-surface-test"),
            new TenantEntry(
                services.GetRequiredService<MutableTenantContext>(),
                services.GetRequiredService<IWorkspaceResolver>()));

        JsonElement body = JsonSerializer.SerializeToElement(arguments, JsonConventions.Options);

        Dictionary<string, JsonElement> named = body.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);

        return await handlers.CallToolAsync(
            new CallToolRequestParams { Name = tool, Arguments = named }, CancellationToken.None);
    }

    private static string Rendered(CallToolResult result) =>
        string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
