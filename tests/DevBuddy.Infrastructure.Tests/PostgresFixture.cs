using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// A real PostgreSQL, started once for the whole suite and migrated from empty.
/// <para>
/// info.md rules out SQLite as an application database, and these tests are why that matters:
/// the generated tsvector column, the GIN index, jsonb storage, and the global query filters do
/// not exist on any substitute. A test that passed against an in-memory provider would be
/// evidence of nothing.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("devbuddy")
        .WithUsername("devbuddy")
        .WithPassword("devbuddy-test-only")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // The exit criterion: the migration applies to an empty database.
        await using DevBuddyDbContext context = CreateContext(new FixedTenantContext(null));
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// A context scoped to one workspace. Passing null produces a context with no tenant, which
    /// is how the fail-closed behaviour of the global query filters is tested.
    /// </summary>
    public DevBuddyDbContext CreateContext(WorkspaceId? workspaceId) =>
        CreateContext(new FixedTenantContext(workspaceId));

    private DevBuddyDbContext CreateContext(ITenantContext tenant)
    {
        DbContextOptions<DevBuddyDbContext> options =
            new DbContextOptionsBuilder<DevBuddyDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

        return new DevBuddyDbContext(options, tenant);
    }

    private sealed class FixedTenantContext(WorkspaceId? workspaceId) : ITenantContext
    {
        public WorkspaceId? WorkspaceId { get; } = workspaceId;
    }
}

/// <summary>
/// One container for the whole assembly. Starting PostgreSQL per test class would turn a fast
/// suite into a slow one for no extra confidence.
/// </summary>
[CollectionDefinition(Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection definitions are named after the collection they define.")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
