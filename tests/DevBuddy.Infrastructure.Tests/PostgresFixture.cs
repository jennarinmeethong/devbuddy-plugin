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
    /// Creates and migrates a database of its own on the same container.
    /// <para>
    /// Needed by the few tests whose subject is the state of the whole database rather than of
    /// one workspace, such as single-workspace mode. Running those against the shared database
    /// would make them depend on how many other tests had run first.
    /// </para>
    /// </summary>
    public async Task<string> CreateIsolatedDatabaseAsync(string name)
    {
        // A database name is an identifier, and identifiers cannot be parameterised. The name is
        // therefore checked against a strict pattern before it reaches the statement, and every
        // caller passes a literal.
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z][a-z0-9_]{2,62}$"))
        {
            throw new ArgumentException($"Refusing to build a create-database statement from {name}.", nameof(name));
        }

        await using (DevBuddyDbContext admin = CreateContext(new FixedTenantContext(null)))
        {
#pragma warning disable EF1002 // Identifiers cannot be parameterised; the name is validated above.
            await admin.Database.ExecuteSqlRawAsync($"create database \"{name}\";");
#pragma warning restore EF1002
        }

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString) { Database = name };

        DbContextOptions<DevBuddyDbContext> options =
            new DbContextOptionsBuilder<DevBuddyDbContext>()
                .UseNpgsql(builder.ConnectionString)
                .Options;

        await using (var context = new DevBuddyDbContext(options, new FixedTenantContext(null)))
        {
            await context.Database.MigrateAsync();
        }

        return builder.ConnectionString;
    }

    /// <summary>A context against a connection string this fixture handed out.</summary>
    public static DevBuddyDbContext CreateContextFor(string connectionString, WorkspaceId? workspaceId)
    {
        DbContextOptions<DevBuddyDbContext> options =
            new DbContextOptionsBuilder<DevBuddyDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new DevBuddyDbContext(options, new FixedTenantContext(workspaceId));
    }

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
