using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Identity;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace DevBuddy.Security.Tests;

/// <summary>
/// The real system: the Application pipeline over the real Infrastructure against a real
/// PostgreSQL.
/// <para>
/// Nothing here is faked. A fake authorization service would prove that the test agrees with the
/// test; these scenarios are only evidence if the membership lookup, the role table, the AI
/// policy, and the query scoping are all the ones that ship.
/// </para>
/// </summary>
public sealed class SecurityFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("devbuddy_security")
        .WithUsername("devbuddy")
        .WithPassword("devbuddy-test-only")
        .Build();

    private ServiceProvider? _services;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var services = new ServiceCollection();

        services.AddDevBuddyInfrastructure(
            _container.GetConnectionString(),
            evidence =>
            {
                // The filesystem provider keeps these tests about access control rather than
                // about object storage, which Phase 3 already covers against real MinIO.
                evidence.Provider = EvidenceStoreProvider.FileSystem;
                evidence.RootPath = Path.Combine(Path.GetTempPath(), "devbuddy-security-" + Guid.NewGuid().ToString("N"));
            });

        services.AddDevBuddyIdentity(identity =>
        {
            identity.SigningKey = "security-tests-signing-key-not-for-production-use";
            identity.MaxFailedAttempts = 3;
            identity.LockoutDuration = TimeSpan.FromMinutes(10);
            identity.MinimumPasswordLength = 12;
        });

        _services = services.BuildServiceProvider();

        using IServiceScope scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevBuddyDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>
    /// A scope with its tenant context already entered, the way a host would set it from the
    /// authenticated principal.
    /// </summary>
    public Session OpenSession(WorkspaceId workspace)
    {
        IServiceScope scope = _services!.CreateScope();
        scope.ServiceProvider.GetRequiredService<MutableTenantContext>().EnterWorkspace(workspace);
        return new Session(scope);
    }

    /// <summary>A scope with no tenant entered, for the fail-closed cases.</summary>
    public Session OpenUnscopedSession() => new(_services!.CreateScope());

    /// <summary>Builds a workspace with two projects, and no memberships at all to start with.</summary>
    public async Task<World> CreateWorldAsync()
    {
        var workspace = WorkspaceId.New();
        var world = new World(workspace, ProjectId.New(), ProjectId.New());

        using Session session = OpenSession(workspace);
        DevBuddyDbContext db = session.Db;

        db.Workspaces.Add(RowMappers.ToRow(new Workspace(workspace, "Acme", world.Founder, World.Now)));
        db.Projects.Add(RowMappers.ToRow(new Project(world.Alpha.ProjectId, workspace, "Alpha", World.Now)));
        db.Projects.Add(RowMappers.ToRow(new Project(world.Beta.ProjectId, workspace, "Beta", World.Now)));
        await db.SaveChangesAsync();

        return world;
    }

    /// <summary>Creates an account with a password, ready to sign in.</summary>
    public async Task<UserId> CreateUserAsync(string email, string password = "correct-horse-battery")
    {
        var userId = UserId.New();

        using Session session = OpenUnscopedSession();
        session.Db.Users.Add(RowMappers.ToRow(new User(userId, email, email, World.Now)));
        await session.Db.SaveChangesAsync();

        await session.Resolve<ICredentialManager>().SetPasswordAsync(userId, password, TestToken.None);
        return userId;
    }

    /// <summary>Grants a role, either workspace-wide or on a single project.</summary>
    public async Task<MembershipId> GrantAsync(
        WorkspaceId workspace, UserId user, Role role, ProjectId? project = null)
    {
        Membership membership = project is { } scoped
            ? Membership.ForProject(
                MembershipId.New(), user, new ProjectScope(workspace, scoped), role, World.Now, user)
            : Membership.ForWorkspace(MembershipId.New(), user, workspace, role, World.Now, user);

        using Session session = OpenSession(workspace);
        await session.Resolve<IAccessDirectory>().AddMembershipAsync(membership, TestToken.None);
        return membership.Id;
    }

    public async Task RevokeAsync(WorkspaceId workspace, MembershipId membershipId)
    {
        using Session session = OpenSession(workspace);
        IAccessDirectory directory = session.Resolve<IAccessDirectory>();

        Membership membership = await directory.FindMembershipAsync(membershipId, TestToken.None)
            ?? throw new InvalidOperationException("The membership was not found.");

        membership.Revoke(World.Now.AddDays(1));
        await directory.UpdateMembershipAsync(membership, TestToken.None);
    }

    public async Task EnableAiAccessAsync(ProjectScope scope, UserId enabledBy)
    {
        using Session session = OpenSession(scope.WorkspaceId);
        IAccessDirectory directory = session.Resolve<IAccessDirectory>();

        Domain.Access.ProjectAiAccessPolicy policy =
            await directory.GetAiAccessPolicyAsync(scope, TestToken.None);

        policy.Enable(enabledBy, World.Now);
        await directory.SaveAiAccessPolicyAsync(policy, TestToken.None);
    }

    public async Task DisableAccountAsync(UserId userId)
    {
        using Session session = OpenUnscopedSession();

        await session.Db.Users
            .Where(user => user.Id == userId.Value)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsDisabled, true));
    }

    /// <summary>Writes a published record and returns it.</summary>
    public async Task<KnowledgeRecord> SeedPublishedRecordAsync(
        ProjectScope scope, WorkItemId workItemId, string title, string body, UserId author)
    {
        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), scope, workItemId, RecordKind.Decision, title, body,
            frontMatter: null,
            new Provenance(ProvenanceSourceKind.HumanAuthored, "seed", "seed", World.Now),
            World.Now, author);

        record.SubmitForApproval(World.Now.AddMinutes(1));
        record.Approve(author, record.CurrentRevision.ContentHash, World.Now.AddMinutes(2));
        record.Publish(World.Now.AddMinutes(3));

        using Session session = OpenSession(scope.WorkspaceId);
        await session.Resolve<IKnowledgeRepository>().AddRecordAsync(record, TestToken.None);
        return record;
    }

    public async Task<WorkItem> SeedWorkItemAsync(ProjectScope scope, string key, UserId author)
    {
        var item = new WorkItem(
            WorkItemId.New(), scope, key, WorkItemType.ChangeRequest,
            "Import normalisation", "Identifiers are normalised first.", World.Now, author);

        using Session session = OpenSession(scope.WorkspaceId);
        session.Db.WorkItems.Add(RowMappers.ToRow(item));
        await session.Db.SaveChangesAsync();
        return item;
    }
}

/// <summary>One dependency-injection scope, with the helpers the tests need on it.</summary>
public sealed class Session(IServiceScope scope) : IDisposable
{
    public IServiceScope Scope { get; } = scope;

    internal DevBuddyDbContext Db => Scope.ServiceProvider.GetRequiredService<DevBuddyDbContext>();

    public T Resolve<T>()
        where T : notnull => Scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>
    /// Runs a use case through the real pipeline as a given caller. Use cases are constructed
    /// here rather than registered, because the pipeline is what is under test, not the container.
    /// </summary>
    public Task<UseCaseResult<TResponse>> RunAsync<TRequest, TResponse>(
        UseCase<TRequest, TResponse> useCase, TRequest request, CallerContext caller)
        where TRequest : IUseCaseRequest =>
        new UseCaseExecutor(
                Resolve<IAuthorizationService>(),
                Resolve<IAuditSink>(),
                Resolve<IRedactor>(),
                Resolve<IClock>())
            .ExecuteAsync(useCase, request, caller, TestToken.None);

    public void Dispose() => Scope.Dispose();
}

/// <summary>One workspace, two projects, and the people in the scenarios.</summary>
public sealed record World(WorkspaceId Workspace, ProjectId AlphaId, ProjectId BetaId)
{
    public static DateTimeOffset Now { get; } = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    public UserId Founder { get; } = UserId.New();

    public ProjectScope Alpha => new(Workspace, AlphaId);

    public ProjectScope Beta => new(Workspace, BetaId);

    public static CallerContext Human(UserId user) => new(user, AccessChannel.Human, "test-human");

    public static CallerContext Ai(UserId user) => new(user, AccessChannel.Ai, "test-ai");
}

internal static class TestToken
{
    public static CancellationToken None => CancellationToken.None;
}

/// <summary>One container for the whole assembly.</summary>
[CollectionDefinition(Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection definitions are named after the collection they define.")]
public sealed class SecurityCollection : ICollectionFixture<SecurityFixture>
{
    public const string Name = "security";
}
