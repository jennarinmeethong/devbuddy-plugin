using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// A PostgreSQL that <b>has</b> the <c>vector</c> extension, for the tests whose subject is the
/// derived vector index.
/// <para>
/// A fixture of its own rather than a change to <see cref="PostgresFixture"/>, and the reason is
/// the point of the whole conditional migration: the shipped stack runs
/// <c>postgres:17-alpine</c>, which does <b>not</b> carry pgvector. Moving the main fixture to a
/// pgvector image would have tested a database no deployment has, and would have quietly stopped
/// proving the thing that matters most here — that <c>migrate</c> still succeeds where the
/// extension is absent. <see cref="EmbeddingIndexTests"/> asserts both sides, each against the
/// server it describes.
/// </para>
/// </summary>
public sealed class PgvectorFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("devbuddy")
        .WithUsername("devbuddy")
        .WithPassword("devbuddy-test-only")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using DevBuddyDbContext context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public DevBuddyDbContext CreateContext()
    {
        DbContextOptions<DevBuddyDbContext> options =
            new DbContextOptionsBuilder<DevBuddyDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

        return new DevBuddyDbContext(options, new NoWorkspace());
    }

    /// <summary>No ambient workspace. Every statement in these tests carries its own scope.</summary>
    private sealed class NoWorkspace : ITenantContext
    {
        public WorkspaceId? WorkspaceId => null;
    }
}

[CollectionDefinition(Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection definitions are named after the collection they define.")]
public sealed class PgvectorCollection : ICollectionFixture<PgvectorFixture>
{
    public const string Name = "pgvector";
}
