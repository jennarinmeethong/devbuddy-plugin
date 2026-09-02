using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevBuddy.Application;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;

namespace DevBuddy.Api.Tests;

/// <summary>
/// The operation surface over HTTP: authentication, role gating, tenant isolation, and the
/// draft-to-published path.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OperationEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task every_operation_route_refuses_an_unauthenticated_caller()
    {
        using HttpClient anonymous = fixture.Factory.CreateClient();

        using HttpResponseMessage listing = await anonymous.GetAsync(new Uri("/operations", UriKind.Relative));

        using HttpResponseMessage invocation = await anonymous.PostAsJsonAsync(
            $"/operations/{UseCaseCatalog.ListProjects.Name}",
            new { workspaceId = fixture.Workspace.Value });

        using HttpResponseMessage evidence = await anonymous.GetAsync(new Uri(
            $"/workspaces/{fixture.Workspace.Value}/projects/{fixture.Project.Value}/evidence/{Guid.NewGuid()}",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, listing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invocation.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, evidence.StatusCode);
    }

    [Fact]
    public async Task the_listing_describes_every_operation_in_the_catalogue()
    {
        using HttpClient client = await fixture.SignInAsAdministratorAsync();

        JsonElement operations = await client.GetFromJsonAsync<JsonElement>("/operations");

        string[] names = [.. operations.EnumerateArray().Select(entry => entry.GetProperty("name").GetString()!)];

        // Everything the catalogue holds except the one that streams bytes: it has a route of
        // its own, and listing it here would invite a caller to POST to a route that cannot
        // serve it.
        string[] dispatchable =
        [
            .. UseCaseCatalog.All
                .Where(descriptor => descriptor.Name != UseCaseCatalog.DownloadEvidence.Name)
                .Select(descriptor => descriptor.Name)
        ];

        Assert.Equal(dispatchable, names);

        // The API describes the AI surface without being it. A human caller can see that
        // publish_record exists and is human-only; an AI caller is never told it exists at all.
        Assert.Equal(
            UseCaseCatalog.AiExposed.Count,
            operations.EnumerateArray().Count(entry => entry.GetProperty("availableToAi").GetBoolean()));
    }

    [Fact]
    public async Task an_administrator_can_list_the_projects_of_their_own_workspace()
    {
        using HttpClient client = await fixture.SignInAsAdministratorAsync();

        (HttpStatusCode status, JsonElement body) = await Invoke(
            client, UseCaseCatalog.ListProjects.Name, new { workspaceId = fixture.Workspace.Value });

        Assert.Equal(HttpStatusCode.OK, status);

        Guid[] projects =
        [
            .. body.GetProperty("projects").EnumerateArray()
                .Select(project => project.GetProperty("projectId").GetGuid())
        ];

        Assert.Contains(fixture.Project.Value, projects);
    }

    [Fact]
    public async Task a_workspace_the_caller_does_not_belong_to_is_refused()
    {
        ProjectScope rival = await fixture.CreateSeparateWorkspaceAsync(UserId.New());

        using HttpClient client = await fixture.SignInAsAdministratorAsync();

        (HttpStatusCode status, _) = await Invoke(
            client, UseCaseCatalog.ListProjects.Name, new { workspaceId = rival.WorkspaceId.Value });

        // Refused, not empty. An administrator of one workspace naming another is a caller
        // reaching outside their tenant, and the answer is no rather than nothing (SB-11).
        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task a_viewer_cannot_publish_and_the_refusal_names_no_resource()
    {
        string email = $"viewer-{Guid.NewGuid():N}@example.test";
        UserId viewer = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(viewer, Role.Viewer);

        using HttpClient client = await fixture.SignInAsync(email, ApiFixture.AdministratorPassword);

        (HttpStatusCode status, JsonElement body) = await Invoke(
            client,
            UseCaseCatalog.PublishRecord.Name,
            new
            {
                scope = new { workspaceId = fixture.Workspace.Value, projectId = fixture.Project.Value },
                recordId = Guid.NewGuid(),
            });

        Assert.Equal(HttpStatusCode.Forbidden, status);

        // The permission check runs before the record is loaded, so a viewer cannot use a refusal
        // to learn whether an identifier they invented happens to exist.
        string detail = body.GetProperty("detail").GetString() ?? string.Empty;
        Assert.DoesNotContain("does not exist", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task an_unknown_operation_is_refused_without_reaching_the_pipeline()
    {
        using HttpClient client = await fixture.SignInAsAdministratorAsync();

        (HttpStatusCode status, _) = await Invoke(client, "exfiltrate_everything", new { });

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task the_streaming_operation_is_not_offered_as_a_json_route()
    {
        using HttpClient client = await fixture.SignInAsAdministratorAsync();

        (HttpStatusCode status, JsonElement body) = await Invoke(
            client,
            UseCaseCatalog.DownloadEvidence.Name,
            new
            {
                scope = new { workspaceId = fixture.Workspace.Value, projectId = fixture.Project.Value },
                evidenceId = Guid.NewGuid(),
            });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("evidence", body.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole lifecycle over HTTP, ending with a stale approval being refused. The domain
    /// invariant is covered by a unit test; what this adds is that the transport cannot route
    /// around it.
    /// </summary>
    [Fact]
    public async Task a_draft_becomes_published_and_a_stale_approval_is_refused()
    {
        WorkItemId workItem = await fixture.SeedWorkItemAsync(fixture.Scope, $"CRQ-{Guid.NewGuid():N}"[..12]);

        using HttpClient client = await fixture.SignInAsAdministratorAsync();

        object scope = new { workspaceId = fixture.Workspace.Value, projectId = fixture.Project.Value };

        (HttpStatusCode created, JsonElement draft) = await Invoke(
            client,
            UseCaseCatalog.CreateDraft.Name,
            new
            {
                scope,
                workItemId = workItem.Value,
                kind = nameof(RecordKind.Decision),
                title = "Identifiers are normalised on the way in",
                body = "We normalise on ingest so every reader sees one shape.",
                provenance = new
                {
                    sourceKind = nameof(ProvenanceSourceKind.HumanAuthored),
                    sourceLocator = "meeting/2026-09-01",
                    author = "integration test",
                    recordedAt = DateTimeOffset.UtcNow,
                },
            });

        Assert.Equal(HttpStatusCode.OK, created);

        Guid recordId = draft.GetProperty("recordId").GetGuid();

        (HttpStatusCode submitted, _) = await Invoke(
            client, UseCaseCatalog.SubmitForApproval.Name, new { scope, recordId });

        Assert.Equal(HttpStatusCode.OK, submitted);

        string firstHash = await CurrentHash(client, scope, recordId);

        // A revision after the approval hash was taken. Approving the old one must not publish
        // the new one.
        (HttpStatusCode revised, _) = await Invoke(
            client,
            UseCaseCatalog.ReviseDraft.Name,
            new
            {
                scope,
                recordId,
                title = "Identifiers are normalised on the way in",
                body = "Rewritten after review, which changes the hash.",
                provenance = new
                {
                    sourceKind = nameof(ProvenanceSourceKind.HumanAuthored),
                    sourceLocator = "meeting/2026-09-02",
                    author = "integration test",
                    recordedAt = DateTimeOffset.UtcNow,
                },
            });

        Assert.Equal(HttpStatusCode.OK, revised);

        (HttpStatusCode stale, _) = await Invoke(
            client,
            UseCaseCatalog.ApproveRecord.Name,
            new { scope, recordId, approvedContentHash = firstHash });

        Assert.Equal(HttpStatusCode.Conflict, stale);

        (HttpStatusCode resubmitted, _) = await Invoke(
            client, UseCaseCatalog.SubmitForApproval.Name, new { scope, recordId });

        Assert.Equal(HttpStatusCode.OK, resubmitted);

        (HttpStatusCode approved, _) = await Invoke(
            client,
            UseCaseCatalog.ApproveRecord.Name,
            new { scope, recordId, approvedContentHash = await CurrentHash(client, scope, recordId) });

        Assert.Equal(HttpStatusCode.OK, approved);

        (HttpStatusCode published, JsonElement result) = await Invoke(
            client, UseCaseCatalog.PublishRecord.Name, new { scope, recordId });

        Assert.Equal(HttpStatusCode.OK, published);
        Assert.Equal(nameof(RecordStatus.Published), result.GetProperty("status").GetString());
    }

    private static async Task<string> CurrentHash(HttpClient client, object scope, Guid recordId)
    {
        (HttpStatusCode status, JsonElement history) = await Invoke(
            client, UseCaseCatalog.ViewRecordHistory.Name, new { scope, recordId });

        Assert.Equal(HttpStatusCode.OK, status);

        return history.GetProperty("revisions").EnumerateArray()
            .OrderByDescending(revision => revision.GetProperty("number").GetInt32())
            .First()
            .GetProperty("contentHash")
            .GetString()!;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Invoke(
        HttpClient client, string operation, object arguments)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/operations/{operation}", arguments);

        JsonElement body = response.Content.Headers.ContentLength is > 0
            ? await response.Content.ReadFromJsonAsync<JsonElement>()
            : default;

        return (response.StatusCode, body);
    }
}
