using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevBuddy.Application;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Hosting;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.McpServer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace DevBuddy.Api.Tests;

/// <summary>
/// The same adapter failure, answered the same way by both hosts.
/// <para>
/// Before the pipeline translated these, the MCP client saw the SDK's generic "An error occurred
/// invoking" and the HTTP API a 500, and neither said whether the caller had reached too far or
/// simply named a repository with nothing mounted. Now the pipeline decides the outcome once, the
/// API maps it onto its status code and the MCP handlers onto their refusal text, and each call
/// leaves a row in the audit trail.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AdapterFailureSurfaceTests(ApiFixture fixture) : IDisposable
{
    private readonly string _analysisRoot =
        Path.Combine(Path.GetTempPath(), "devbuddy-api-analysis-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_analysisRoot))
        {
            Directory.Delete(_analysisRoot, recursive: true);
        }
    }

    [Fact]
    public async Task a_path_escape_is_forbidden_over_http_refused_over_mcp_and_audited_both_times()
    {
        ProjectScope project = await fixture.CreateProjectAsync($"Esc {Guid.NewGuid():N}"[..12]);
        await fixture.EnableAiAccessAsync(project);
        Directory.CreateDirectory(Path.Combine(_analysisRoot, project.ProjectId.Value.ToString()));

        await using WebApplicationFactory<ApiHost> host = fixture.BuildWith(("Analysis:RootPath", _analysisRoot));

        object arguments = new { scope = Scope(project), target = "../../../etc" };

        using HttpClient human = await SignInAsync(host);
        using HttpResponseMessage response = await human.PostAsJsonAsync(
            $"/operations/{UseCaseCatalog.AnalyzeCode.Name}", arguments);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("outside", await DetailAsync(response), StringComparison.Ordinal);

        CallToolResult tool = await CallToolAsync(host, project, UseCaseCatalog.AnalyzeCode.Name, arguments);

        Assert.True(tool.IsError);
        Assert.StartsWith("Refused:", Rendered(tool), StringComparison.Ordinal);
        Assert.Contains("outside", Rendered(tool), StringComparison.Ordinal);

        Assert.Equal(2, await CountAsync(host, project, AuditAction.AccessDenied, AuditOutcome.Denied));
    }

    [Theory]
    [InlineData("analyze_change_impact", (int)AuditAction.AnalysisRun)]
    [InlineData("compare_snapshots", (int)AuditAction.RecordViewed)]
    public async Task a_repository_with_no_working_copy_is_not_found_on_both_hosts_and_audited_both_times(
        string operation, int action)
    {
        ProjectScope project = await fixture.CreateProjectAsync($"Nwc {Guid.NewGuid():N}"[..12]);
        await fixture.EnableAiAccessAsync(project);

        await using WebApplicationFactory<ApiHost> host = fixture.BuildWith(("Analysis:RootPath", _analysisRoot));

        object arguments = new
        {
            scope = Scope(project),
            repositoryId = Guid.NewGuid(),
            commitOrRange = "main",
            earlierReference = "main",
            laterReference = "release",
        };

        using HttpClient human = await SignInAsync(host);
        using HttpResponseMessage response = await human.PostAsJsonAsync($"/operations/{operation}", arguments);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("No working copy is mounted", await DetailAsync(response), StringComparison.Ordinal);

        CallToolResult tool = await CallToolAsync(host, project, operation, arguments);

        Assert.True(tool.IsError);
        Assert.StartsWith("Not found: No working copy is mounted", Rendered(tool), StringComparison.Ordinal);

        Assert.Equal(2, await CountAsync(host, project, (AuditAction)action, AuditOutcome.Failed));
    }

    private static object Scope(ProjectScope project) => new
    {
        workspaceId = project.WorkspaceId.Value,
        projectId = project.ProjectId.Value,
    };

    private async Task<HttpClient> SignInAsync(WebApplicationFactory<ApiHost> host)
    {
        using HttpClient anonymous = host.CreateClient();

        using HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            "/auth/sign-in", new { email = fixture.AdministratorEmail, password = ApiFixture.AdministratorPassword });

        response.EnsureSuccessStatusCode();

        JsonElement tokens = await response.Content.ReadFromJsonAsync<JsonElement>();

        HttpClient client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());

        return client;
    }

    private static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("detail").GetString() ?? string.Empty;
    }

    /// <summary>The real MCP handlers, over the same host's container, on the AI channel.</summary>
    private async Task<CallToolResult> CallToolAsync(
        WebApplicationFactory<ApiHost> host, ProjectScope project, string tool, object arguments)
    {
        using IServiceScope scope = host.Services.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        services.GetRequiredService<MutableTenantContext>().EnterWorkspace(project.WorkspaceId);

        var handlers = new McpToolHandlers(
            ApiFixture.DispatcherFor(scope),
            new CallerContext(fixture.Administrator, AccessChannel.Ai, "adapter-failure-test"),
            new TenantEntry(
                services.GetRequiredService<MutableTenantContext>(),
                services.GetRequiredService<IWorkspaceResolver>()));

        JsonElement body = JsonSerializer.SerializeToElement(arguments, JsonConventions.Options);

        Dictionary<string, JsonElement> named = body.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);

        return await handlers.CallToolAsync(
            new CallToolRequestParams { Name = tool, Arguments = named }, CancellationToken.None);
    }

    private static async Task<int> CountAsync(
        WebApplicationFactory<ApiHost> host, ProjectScope project, AuditAction action, AuditOutcome outcome)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<MutableTenantContext>().EnterWorkspace(project.WorkspaceId);

        return await scope.ServiceProvider.GetRequiredService<DevBuddyDbContext>().AuditEvents
            .AsNoTracking()
            .CountAsync(row =>
                row.ProjectId == project.ProjectId.Value
                && row.Action == (int)action
                && row.Outcome == (int)outcome);
    }

    private static string Rendered(CallToolResult result) =>
        string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
