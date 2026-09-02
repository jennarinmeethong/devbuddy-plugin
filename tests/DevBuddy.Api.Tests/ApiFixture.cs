using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Dispatch;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Administration;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Mapping;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace DevBuddy.Api.Tests;

/// <summary>
/// The real API, hosted in-process over a real PostgreSQL.
/// <para>
/// <c>WebApplicationFactory&lt;Program&gt;</c> runs the application's own <c>Program.cs</c>: the
/// same authentication, the same rate limiter, the same routes, the same pipeline. A test harness
/// that re-composed the services would be testing an application nobody deploys, and the things
/// most worth proving here — that a route is authorised, that a token is validated — are exactly
/// the things such a harness would quietly leave out.
/// </para>
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string AdministratorPassword = "correct-horse-battery-staple";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("devbuddy_api")
        .WithUsername("devbuddy")
        .WithPassword("devbuddy-test-only")
        .Build();

    private readonly string _evidenceRoot =
        Path.Combine(Path.GetTempPath(), "devbuddy-api-" + Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<ApiHost>? _factory;

    /// <summary>The bootstrapped administrator, and the workspace and project they own.</summary>
    public UserId Administrator { get; private set; }

    public string AdministratorEmail { get; } = $"admin-{Guid.NewGuid():N}@example.test";

    public WorkspaceId Workspace { get; private set; }

    public ProjectId Project { get; private set; }

    public ProjectScope Scope => new(Workspace, Project);

    public WebApplicationFactory<ApiHost> Factory =>
        _factory ?? throw new InvalidOperationException("The fixture has not been initialised.");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _factory = Build([]);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DevBuddyDbContext>()
                .Database.MigrateAsync();
        }

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            BootstrapResult result = await scope.ServiceProvider
                .GetRequiredService<IInstallationBootstrapper>()
                .BootstrapAsync(
                    new BootstrapRequest("Acme", AdministratorEmail, AdministratorPassword, "Alpha"),
                    CancellationToken.None);

            Assert.True(result.Created, result.Reason);

            Administrator = result.AdministratorId!.Value;
            Workspace = result.WorkspaceId!.Value;
            Project = result.ProjectId!.Value;
        }
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _container.DisposeAsync();

        if (Directory.Exists(_evidenceRoot))
        {
            Directory.Delete(_evidenceRoot, recursive: true);
        }
    }

    /// <summary>
    /// A second instance of the same application against the same database, with settings of its
    /// own. Used by the rate-limiting test, whose subject is a startup-time setting and which
    /// would otherwise have to spend the shared instance's budget to observe anything.
    /// </summary>
    public WebApplicationFactory<ApiHost> BuildWith(params (string Key, string Value)[] settings) =>
        Build(settings);

    /// <summary>Signs in and returns a client carrying the bearer token.</summary>
    public async Task<HttpClient> SignInAsync(string email, string password)
    {
        using HttpClient anonymous = Factory.CreateClient();

        using HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            "/auth/sign-in", new { email, password });

        response.EnsureSuccessStatusCode();

        JsonElement tokens = await response.Content.ReadFromJsonAsync<JsonElement>();

        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());

        return client;
    }

    public Task<HttpClient> SignInAsAdministratorAsync() =>
        SignInAsync(AdministratorEmail, AdministratorPassword);

    /// <summary>
    /// Creates an account directly.
    /// <para>
    /// There is no operation for it, and that is a real gap rather than a testing shortcut: the
    /// catalogue can grant a membership to a user who exists but has no way to bring one into
    /// being. Recorded in the Phase 7 status; until it is closed, the tests seed accounts the way
    /// an operator would have to, through the database.
    /// </para>
    /// </summary>
    public async Task<UserId> CreateUserAsync(string email, string password = AdministratorPassword)
    {
        var userId = UserId.New();

        using IServiceScope scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevBuddyDbContext>();

        db.Users.Add(RowMappers.ToRow(new User(userId, email, email, DateTimeOffset.UtcNow)));
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<ICredentialManager>()
            .SetPasswordAsync(userId, password, CancellationToken.None);

        return userId;
    }

    /// <summary>Grants a role, workspace-wide or on one project.</summary>
    public async Task<MembershipId> GrantAsync(UserId user, Role role, ProjectId? project = null)
    {
        Membership membership = project is { } scoped
            ? Membership.ForProject(
                MembershipId.New(), user, new ProjectScope(Workspace, scoped), role,
                DateTimeOffset.UtcNow, Administrator)
            : Membership.ForWorkspace(
                MembershipId.New(), user, Workspace, role, DateTimeOffset.UtcNow, Administrator);

        using IServiceScope scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<MutableTenantContext>().EnterWorkspace(Workspace);

        await scope.ServiceProvider.GetRequiredService<IAccessDirectory>()
            .AddMembershipAsync(membership, CancellationToken.None);

        return membership.Id;
    }

    /// <summary>
    /// Another project in the same workspace, so a test whose subject is the AI access policy
    /// starts from the default rather than from whatever an earlier test left behind. AI access
    /// is off for it, because off is what absence means.
    /// </summary>
    public async Task<ProjectScope> CreateProjectAsync(string name)
    {
        var projectId = ProjectId.New();

        using IServiceScope scope = OpenScope(Workspace);
        var db = scope.ServiceProvider.GetRequiredService<DevBuddyDbContext>();

        db.Projects.Add(RowMappers.ToRow(
            new Project(projectId, Workspace, name, DateTimeOffset.UtcNow)));

        await db.SaveChangesAsync();

        return new ProjectScope(Workspace, projectId);
    }

    /// <summary>Adds a second workspace with a project, for the isolation cases.</summary>
    public async Task<ProjectScope> CreateSeparateWorkspaceAsync(UserId owner)
    {
        var workspace = WorkspaceId.New();
        var project = ProjectId.New();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using IServiceScope scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<MutableTenantContext>().EnterWorkspace(workspace);

        var db = scope.ServiceProvider.GetRequiredService<DevBuddyDbContext>();
        db.Workspaces.Add(RowMappers.ToRow(new Workspace(workspace, "Rival", owner, now)));
        db.Projects.Add(RowMappers.ToRow(new Project(project, workspace, "Beta", now)));
        await db.SaveChangesAsync();

        return new ProjectScope(workspace, project);
    }

    /// <summary>
    /// Seeds a work item, which every knowledge record hangs off. No operation creates one —
    /// the same gap as accounts, noted with the Phase 7 status.
    /// </summary>
    public async Task<WorkItemId> SeedWorkItemAsync(ProjectScope scope, string key)
    {
        var item = new WorkItem(
            WorkItemId.New(), scope, key, WorkItemType.ChangeRequest,
            "Import normalisation", "Identifiers are normalised on the way in.",
            DateTimeOffset.UtcNow, Administrator);

        using IServiceScope container = OpenScope(scope.WorkspaceId);
        var db = container.ServiceProvider.GetRequiredService<DevBuddyDbContext>();

        db.WorkItems.Add(RowMappers.ToRow(item));
        await db.SaveChangesAsync();

        return item.Id;
    }

    /// <summary>Turns AI access on for a project. Off is the default and is never written here.</summary>
    public async Task EnableAiAccessAsync(ProjectScope scope)
    {
        using IServiceScope container = Factory.Services.CreateScope();
        container.ServiceProvider.GetRequiredService<MutableTenantContext>()
            .EnterWorkspace(scope.WorkspaceId);

        var directory = container.ServiceProvider.GetRequiredService<IAccessDirectory>();

        ProjectAiAccessPolicy policy = await directory.GetAiAccessPolicyAsync(
            scope, CancellationToken.None);

        policy.Enable(Administrator, DateTimeOffset.UtcNow);
        await directory.SaveAiAccessPolicyAsync(policy, CancellationToken.None);
    }

    /// <summary>
    /// A scope on the same container, with the workspace entered, for driving the MCP handlers
    /// against the same data the HTTP tests see.
    /// </summary>
    public IServiceScope OpenScope(WorkspaceId workspace)
    {
        IServiceScope scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<MutableTenantContext>().EnterWorkspace(workspace);
        return scope;
    }

    public static OperationDispatcher DispatcherFor(IServiceScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.ServiceProvider.GetRequiredService<OperationDispatcher>();
    }

    private WebApplicationFactory<ApiHost> Build(IReadOnlyList<(string Key, string Value)> overrides)
    {
        var factory = new WebApplicationFactory<ApiHost>();

        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DevBuddy", _container.GetConnectionString());
            builder.UseSetting("Identity:SigningKey", "api-integration-tests-signing-key-not-for-production");
            builder.UseSetting("Identity:MinimumPasswordLength", "12");
            builder.UseSetting("Identity:MaxFailedAttempts", "3");

            // The filesystem provider keeps these tests about the HTTP surface. Object storage
            // has its own integration tests against real MinIO in Phase 3.
            builder.UseSetting("Evidence:Provider", "FileSystem");
            builder.UseSetting("Evidence:RootPath", _evidenceRoot);

            // Generous by default so ordinary tests are not competing for the budget; the
            // rate-limiting test builds an instance with a small one.
            builder.UseSetting("RateLimiting:AuthPermitLimit", "1000");

            foreach ((string key, string value) in overrides)
            {
                builder.UseSetting(key, value);
            }
        });
    }
}

/// <summary>One container and one hosted application for the whole assembly.</summary>
[CollectionDefinition(Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection definitions are named after the collection they define.")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
