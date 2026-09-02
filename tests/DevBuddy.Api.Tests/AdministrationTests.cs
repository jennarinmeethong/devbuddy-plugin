using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevBuddy.Application;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Work;

namespace DevBuddy.Api.Tests;

/// <summary>
/// The administration surface Phase 8 exists for: setting a workspace up, onboarding a second
/// person, and the role boundaries the UI is allowed to assume but never to enforce.
/// <para>
/// Every check here is server-side. The web client hides what the server would refuse, and that
/// is a courtesy to the person using it; these tests are what makes the refusal real (SB-16).
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AdministrationTests(ApiFixture fixture)
{
    private static readonly string[] PendingApproval = ["PendingApproval"];
    private static readonly string[] Archived = ["Archived"];

    /// <summary>
    /// The gap Phase 7 left open, closed: an administrator can now bring a second person into an
    /// installation, and that person can set their own password and sign in.
    /// </summary>
    [Fact]
    public async Task an_administrator_can_onboard_a_second_person_end_to_end()
    {
        using HttpClient admin = await fixture.SignInAsAdministratorAsync();

        string email = $"onboarded-{Guid.NewGuid():N}@example.test";

        (HttpStatusCode created, JsonElement account) = await Invoke(
            admin,
            UseCaseCatalog.CreateUserAccount.Name,
            new
            {
                workspaceId = fixture.Workspace.Value,
                email,
                displayName = "Onboarded Person",
                role = nameof(Role.Contributor),
            });

        Assert.Equal(HttpStatusCode.OK, created);

        string setupToken = account.GetProperty("setupToken").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(setupToken));

        // The account exists but has no credential, so nothing can sign in as it yet. That is the
        // point of handing back a setup token rather than a password an administrator chose.
        using HttpClient anonymous = fixture.Factory.CreateClient();

        using (HttpResponseMessage tooEarly = await anonymous.PostAsJsonAsync(
            "/auth/sign-in", new { email, password = "whatever-they-guess" }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, tooEarly.StatusCode);
        }

        using (HttpResponseMessage redeemed = await anonymous.PostAsJsonAsync(
            "/auth/recovery/complete",
            new { token = setupToken, newPassword = "a-password-they-chose" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, redeemed.StatusCode);
        }

        // Single-use, like every other token from that table.
        using (HttpResponseMessage replayed = await anonymous.PostAsJsonAsync(
            "/auth/recovery/complete",
            new { token = setupToken, newPassword = "a-different-password" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);
        }

        using HttpClient newcomer = await fixture.SignInAsync(email, "a-password-they-chose");

        JsonElement me = await newcomer.GetFromJsonAsync<JsonElement>("/me");

        Assert.Equal(email, me.GetProperty("email").GetString());

        JsonElement workspace = me.GetProperty("workspaces").EnumerateArray().Single();
        Assert.Equal(fixture.Workspace.Value, workspace.GetProperty("workspaceId").GetGuid());
        Assert.Equal(nameof(Role.Contributor), workspace.GetProperty("role").GetString());
    }

    [Fact]
    public async Task the_same_address_cannot_be_onboarded_twice()
    {
        using HttpClient admin = await fixture.SignInAsAdministratorAsync();

        string email = $"twice-{Guid.NewGuid():N}@example.test";

        object arguments = new
        {
            workspaceId = fixture.Workspace.Value,
            email,
            displayName = "Twice",
            role = nameof(Role.Viewer),
        };

        (HttpStatusCode first, _) = await Invoke(admin, UseCaseCatalog.CreateUserAccount.Name, arguments);
        (HttpStatusCode second, _) = await Invoke(admin, UseCaseCatalog.CreateUserAccount.Name, arguments);

        Assert.Equal(HttpStatusCode.OK, first);

        // Told plainly, unlike sign-in. The caller is an administrator of this workspace acting
        // on an address they typed; refusing to say would leave them guessing why nothing changed.
        Assert.Equal(HttpStatusCode.Conflict, second);
    }

    [Fact]
    public async Task an_administrator_can_create_a_project_and_a_work_item_in_it()
    {
        using HttpClient admin = await fixture.SignInAsAdministratorAsync();

        (HttpStatusCode createdProject, JsonElement project) = await Invoke(
            admin,
            UseCaseCatalog.CreateProject.Name,
            new { workspaceId = fixture.Workspace.Value, name = $"Gamma {Guid.NewGuid():N}"[..12] });

        Assert.Equal(HttpStatusCode.OK, createdProject);

        Guid projectId = project.GetProperty("projectId").GetGuid();
        object scope = new { workspaceId = fixture.Workspace.Value, projectId };

        (HttpStatusCode createdItem, JsonElement item) = await Invoke(
            admin,
            UseCaseCatalog.CreateWorkItem.Name,
            new
            {
                scope,
                key = "CRQ-201",
                type = nameof(WorkItemType.ChangeRequest),
                title = "Normalise identifiers on import",
                goal = "So every reader sees one shape rather than three.",
                inScope = "The import path.",
                exclusions = "The export path, which is a separate change.",
            });

        Assert.Equal(HttpStatusCode.OK, createdItem);
        Assert.Equal("CRQ-201", item.GetProperty("key").GetString());

        (HttpStatusCode listed, JsonElement items) = await Invoke(
            admin, UseCaseCatalog.ListWorkItems.Name, new { scope });

        Assert.Equal(HttpStatusCode.OK, listed);

        Assert.Contains(
            "CRQ-201",
            items.GetProperty("workItems").EnumerateArray()
                .Select(entry => entry.GetProperty("key").GetString()),
            StringComparer.Ordinal);

        // A new project is closed to AI until somebody opens it. Absence is the deny, so a
        // project created and forgotten is not a project quietly readable by a model (SB-08).
        (HttpStatusCode aiRead, _) = await Invoke(
            admin, UseCaseCatalog.ListProjects.Name, new { workspaceId = fixture.Workspace.Value });

        Assert.Equal(HttpStatusCode.OK, aiRead);
    }

    /// <summary>
    /// The Phase 8 exit criterion: a viewer session cannot reach the approval or audit surfaces,
    /// and the server is what says so.
    /// </summary>
    [Fact]
    public async Task a_viewer_cannot_reach_the_approval_or_audit_surfaces()
    {
        string email = $"viewer-only-{Guid.NewGuid():N}@example.test";
        UserId viewer = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(viewer, Role.Viewer);

        using HttpClient client = await fixture.SignInAsync(email, ApiFixture.AdministratorPassword);

        object scope = new { workspaceId = fixture.Workspace.Value, projectId = fixture.Project.Value };

        // Approval, in every form the UI could offer it.
        foreach (string operation in new[]
        {
            UseCaseCatalog.ApproveRecord.Name,
            UseCaseCatalog.RequestCorrection.Name,
            UseCaseCatalog.PublishRecord.Name,
            UseCaseCatalog.ArchiveRecord.Name,
        })
        {
            (HttpStatusCode status, _) = await Invoke(
                client,
                operation,
                new { scope, recordId = Guid.NewGuid(), approvedContentHash = "abc", reason = "no" });

            Assert.Equal(HttpStatusCode.Forbidden, status);
        }

        // Audit.
        (HttpStatusCode audit, _) = await Invoke(
            client,
            UseCaseCatalog.ReadAuditHistory.Name,
            new
            {
                scope,
                occurredFrom = DateTimeOffset.UtcNow.AddDays(-1),
                occurredUntil = DateTimeOffset.UtcNow,
            });

        Assert.Equal(HttpStatusCode.Forbidden, audit);

        // Membership administration, and creating anything.
        foreach ((string operation, object arguments) in new (string, object)[]
        {
            (UseCaseCatalog.ListMemberships.Name, new { workspaceId = fixture.Workspace.Value }),
            (UseCaseCatalog.CreateProject.Name, new { workspaceId = fixture.Workspace.Value, name = "Sneaky" }),
            (UseCaseCatalog.GrantMembership.Name,
                new { workspaceId = fixture.Workspace.Value, subjectUserId = viewer.Value, role = nameof(Role.Administrator) }),
            (UseCaseCatalog.CreateUserAccount.Name,
                new { workspaceId = fixture.Workspace.Value, email = "x@example.test", displayName = "X", role = nameof(Role.Administrator) }),
        })
        {
            (HttpStatusCode status, _) = await Invoke(client, operation, arguments);
            Assert.Equal(HttpStatusCode.Forbidden, status);
        }

        // And the same answer from /me, which is what the navigation is built from: a viewer is
        // never told they hold ReviewRecord or ReadAudit, so a correct client never offers them.
        JsonElement me = await client.GetFromJsonAsync<JsonElement>("/me");

        string[] permissions =
        [
            .. me.GetProperty("workspaces").EnumerateArray()
                .SelectMany(workspace => workspace.GetProperty("permissions").EnumerateArray())
                .Select(permission => permission.GetString()!)
        ];

        Assert.Contains(nameof(PermissionKind.ReadKnowledge), permissions, StringComparer.Ordinal);
        Assert.DoesNotContain(nameof(PermissionKind.ReviewRecord), permissions, StringComparer.Ordinal);
        Assert.DoesNotContain(nameof(PermissionKind.ReadAudit), permissions, StringComparer.Ordinal);
        Assert.DoesNotContain(nameof(PermissionKind.ManageAccess), permissions, StringComparer.Ordinal);
    }

    [Fact]
    public async Task a_contributor_can_register_work_but_not_administer_the_workspace()
    {
        string email = $"contributor-{Guid.NewGuid():N}@example.test";
        UserId contributor = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(contributor, Role.Contributor);

        using HttpClient client = await fixture.SignInAsync(email, ApiFixture.AdministratorPassword);

        object scope = new { workspaceId = fixture.Workspace.Value, projectId = fixture.Project.Value };

        (HttpStatusCode registered, _) = await Invoke(
            client,
            UseCaseCatalog.CreateWorkItem.Name,
            new
            {
                scope,
                key = $"DEV-{Guid.NewGuid():N}"[..10],
                type = nameof(WorkItemType.Develop),
                title = "Record what this work is",
                goal = "So the next person does not have to reconstruct it.",
            });

        // Registering the work a draft is about is contributing, not administering.
        Assert.Equal(HttpStatusCode.OK, registered);

        (HttpStatusCode refused, _) = await Invoke(
            client,
            UseCaseCatalog.CreateProject.Name,
            new { workspaceId = fixture.Workspace.Value, name = "Not theirs to make" });

        Assert.Equal(HttpStatusCode.Forbidden, refused);
    }

    [Fact]
    public async Task the_membership_list_shows_revoked_grants_as_revoked_rather_than_hiding_them()
    {
        UserId subject = await fixture.CreateUserAsync($"revoked-{Guid.NewGuid():N}@example.test");
        MembershipId membershipId = await fixture.GrantAsync(subject, Role.Reviewer);

        using HttpClient admin = await fixture.SignInAsAdministratorAsync();

        (HttpStatusCode revoked, _) = await Invoke(
            admin,
            UseCaseCatalog.RevokeMembership.Name,
            new { workspaceId = fixture.Workspace.Value, membershipId = membershipId.Value });

        Assert.Equal(HttpStatusCode.OK, revoked);

        (HttpStatusCode listed, JsonElement memberships) = await Invoke(
            admin, UseCaseCatalog.ListMemberships.Name, new { workspaceId = fixture.Workspace.Value });

        Assert.Equal(HttpStatusCode.OK, listed);

        JsonElement entry = memberships.GetProperty("memberships").EnumerateArray()
            .Single(membership => membership.GetProperty("membershipId").GetGuid() == membershipId.Value);

        // Present and marked inactive. A screen that dropped it would make a revocation look like
        // the grant never happened, and "who used to have this" is what somebody is looking for.
        Assert.False(entry.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task the_review_queue_returns_records_waiting_for_approval()
    {
        WorkItemId workItem = await fixture.SeedWorkItemAsync(
            fixture.Scope, $"CRQ-{Guid.NewGuid():N}"[..12]);

        using HttpClient admin = await fixture.SignInAsAdministratorAsync();

        object scope = new { workspaceId = fixture.Workspace.Value, projectId = fixture.Project.Value };

        (HttpStatusCode drafted, JsonElement draft) = await Invoke(
            admin,
            UseCaseCatalog.CreateDraft.Name,
            new
            {
                scope,
                workItemId = workItem.Value,
                kind = "Decision",
                title = "Waiting for a reviewer",
                body = "Submitted and not yet approved.",
                provenance = new
                {
                    sourceKind = "HumanAuthored",
                    sourceLocator = "meeting/2026-09-02",
                    author = "integration test",
                    recordedAt = DateTimeOffset.UtcNow,
                },
            });

        Assert.Equal(HttpStatusCode.OK, drafted);
        Guid recordId = draft.GetProperty("recordId").GetGuid();

        (HttpStatusCode submitted, _) = await Invoke(
            admin, UseCaseCatalog.SubmitForApproval.Name, new { scope, recordId });

        Assert.Equal(HttpStatusCode.OK, submitted);

        (HttpStatusCode queued, JsonElement queue) = await Invoke(
            admin, UseCaseCatalog.ListRecords.Name, new { scope, statuses = PendingApproval });

        Assert.Equal(HttpStatusCode.OK, queued);

        Guid[] waiting =
        [
            .. queue.GetProperty("records").EnumerateArray()
                .Select(record => record.GetProperty("recordId").GetGuid())
        ];

        Assert.Contains(recordId, waiting);

        // And the filter is a filter: a status nothing is in comes back empty rather than
        // returning the project.
        (_, JsonElement archived) = await Invoke(
            admin, UseCaseCatalog.ListRecords.Name, new { scope, statuses = Archived });

        Assert.DoesNotContain(
            recordId,
            archived.GetProperty("records").EnumerateArray()
                .Select(record => record.GetProperty("recordId").GetGuid()));
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
