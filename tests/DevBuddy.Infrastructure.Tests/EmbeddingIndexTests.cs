using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Search;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The derived vector index, against a real PostgreSQL with pgvector (ADR-0012).
/// <para>
/// Two things here are worth more than the rest. The first is that <b>a project's vectors never
/// come back for another project's query</b>, proved by seeding two projects with identical
/// vectors and asking each — identical on purpose, because a distance-based query has every
/// numerical reason to return the other project's row and only the <c>where</c> clause stopping
/// it. The second is that <b>the migration still applies where pgvector is absent</b>, which is
/// the condition the shipped <c>postgres:17-alpine</c> stack is actually in and the reason the
/// migration is written the way it is.
/// </para>
/// </summary>
[Collection(PgvectorCollection.Name)]
public sealed class EmbeddingIndexTests(PgvectorFixture fixture)
{
    private const string Model = "test-embedding-3-small";

    private readonly PgvectorFixture _fixture = fixture;

    [Fact]
    public async Task the_index_is_available_where_the_extension_is()
    {
        await using DevBuddyDbContext context = _fixture.CreateContext();
        PostgresEmbeddingIndex index = new(context);

        Assert.True(await index.IsAvailableAsync(CancellationToken.None));
    }

    /// <summary>
    /// The condition every shipped deployment is in today, and the reason the migration is guarded:
    /// <c>postgres:17-alpine</c> has no pgvector, <c>migrate</c> must still succeed, and the index
    /// must say it is absent rather than fail at the first write.
    /// </summary>
    [Fact]
    public async Task the_migration_applies_and_the_index_is_absent_where_the_extension_is_not()
    {
        // The shared fixture is postgres:17-alpine and is migrated in its own initialiser, so
        // reaching it here is the assertion: migrate ran, and this table is not there.
        await using PostgresFixture plain = new();
        await plain.InitializeAsync();

        try
        {
            await using DevBuddyDbContext context = plain.CreateContext(null);
            PostgresEmbeddingIndex index = new(context);

            Assert.False(await index.IsAvailableAsync(CancellationToken.None));

            // And a purge on that server is a no-op rather than an error, because delete_project
            // calls it on every installation.
            Assert.Equal(0, await index.PurgeProjectAsync(Scope(), CancellationToken.None));
        }
        finally
        {
            await plain.DisposeAsync();
        }
    }

    [Fact]
    public async Task a_vector_comes_back_as_its_own_nearest_neighbour()
    {
        await using DevBuddyDbContext context = _fixture.CreateContext();
        PostgresEmbeddingIndex index = new(context);
        ProjectScope scope = Scope();

        KnowledgeRecordId near = KnowledgeRecordId.New();
        KnowledgeRecordId far = KnowledgeRecordId.New();

        await index.UpsertAsync(
            scope,
            Model,
            [
                new EmbeddedRevision(near, 1, "hash-near", Vector(1f, 0f, 0f)),
                new EmbeddedRevision(far, 1, "hash-far", Vector(0f, 0f, 1f)),
            ],
            CancellationToken.None);

        IReadOnlyList<SimilarRevision> hits = await index.FindSimilarAsync(
            scope, Model, Vector(1f, 0f, 0f), 10, CancellationToken.None);

        Assert.Equal(2, hits.Count);
        Assert.Equal(near, hits[0].RecordId);
        Assert.Equal(far, hits[1].RecordId);

        // Cosine distance: identical is 0, orthogonal is 1. Asserted loosely, because the point
        // is the ordering and the metric, not floating-point equality.
        Assert.True(hits[0].Distance < 0.001, $"Expected ~0 but got {hits[0].Distance}.");
        Assert.True(hits[1].Distance > 0.9, $"Expected ~1 but got {hits[1].Distance}.");
    }

    /// <summary>
    /// The isolation test, and the reason the vectors are identical: every numerical reason to
    /// return the other project's row is present, and only the <c>where</c> clause refuses. A
    /// post-filter would have computed and ranked both before deciding.
    /// </summary>
    [Fact]
    public async Task a_query_never_reaches_another_projects_vectors()
    {
        await using DevBuddyDbContext context = _fixture.CreateContext();
        PostgresEmbeddingIndex index = new(context);

        WorkspaceId workspace = new(Guid.NewGuid());
        ProjectScope mine = new(workspace, new ProjectId(Guid.NewGuid()));
        ProjectScope theirs = new(workspace, new ProjectId(Guid.NewGuid()));

        KnowledgeRecordId ours = KnowledgeRecordId.New();
        KnowledgeRecordId hidden = KnowledgeRecordId.New();

        await index.UpsertAsync(
            mine, Model, [new EmbeddedRevision(ours, 1, "h1", Vector(1f, 0f, 0f))], CancellationToken.None);

        await index.UpsertAsync(
            theirs, Model, [new EmbeddedRevision(hidden, 1, "h2", Vector(1f, 0f, 0f))], CancellationToken.None);

        IReadOnlyList<SimilarRevision> hits = await index.FindSimilarAsync(
            mine, Model, Vector(1f, 0f, 0f), 10, CancellationToken.None);

        SimilarRevision only = Assert.Single(hits);
        Assert.Equal(ours, only.RecordId);
        Assert.NotEqual(hidden, only.RecordId);
    }

    /// <summary>
    /// A second workspace's vectors are equally unreachable. The workspace is in the same
    /// <c>where</c> clause as the project, so a project identifier guessed or reused across
    /// workspaces does not open anything.
    /// </summary>
    [Fact]
    public async Task a_query_never_reaches_another_workspaces_vectors()
    {
        await using DevBuddyDbContext context = _fixture.CreateContext();
        PostgresEmbeddingIndex index = new(context);

        ProjectId shared = new(Guid.NewGuid());
        ProjectScope mine = new(new WorkspaceId(Guid.NewGuid()), shared);
        ProjectScope theirs = new(new WorkspaceId(Guid.NewGuid()), shared);

        KnowledgeRecordId ours = KnowledgeRecordId.New();

        await index.UpsertAsync(
            mine, Model, [new EmbeddedRevision(ours, 1, "h1", Vector(0f, 1f, 0f))], CancellationToken.None);

        await index.UpsertAsync(
            theirs,
            Model,
            [new EmbeddedRevision(KnowledgeRecordId.New(), 1, "h2", Vector(0f, 1f, 0f))],
            CancellationToken.None);

        IReadOnlyList<SimilarRevision> hits = await index.FindSimilarAsync(
            mine, Model, Vector(0f, 1f, 0f), 10, CancellationToken.None);

        Assert.Equal(ours, Assert.Single(hits).RecordId);
    }

    /// <summary>
    /// Two models in one project, and neither answers the other's question. Comparing across
    /// models does not fail — it returns a confident ranking of nonsense, which is why the model
    /// is in the key and in the query.
    /// </summary>
    [Fact]
    public async Task a_query_never_compares_vectors_from_a_different_model()
    {
        await using DevBuddyDbContext context = _fixture.CreateContext();
        PostgresEmbeddingIndex index = new(context);
        ProjectScope scope = Scope();

        KnowledgeRecordId fromA = KnowledgeRecordId.New();
        KnowledgeRecordId fromB = KnowledgeRecordId.New();

        await index.UpsertAsync(
            scope, "model-a", [new EmbeddedRevision(fromA, 1, "h1", Vector(1f, 0f, 0f))], CancellationToken.None);

        await index.UpsertAsync(
            scope, "model-b", [new EmbeddedRevision(fromB, 1, "h2", Vector(1f, 0f, 0f))], CancellationToken.None);

        Assert.Equal(
            fromA,
            Assert.Single(await index.FindSimilarAsync(
                scope, "model-a", Vector(1f, 0f, 0f), 10, CancellationToken.None)).RecordId);

        Assert.Equal(
            fromB,
            Assert.Single(await index.FindSimilarAsync(
                scope, "model-b", Vector(1f, 0f, 0f), 10, CancellationToken.None)).RecordId);
    }

    /// <summary>
    /// A model renamed across an upgrade, keeping its name and changing its dimension, is the case
    /// dimension matching catches. pgvector raises on a mismatched comparison, and an exception
    /// from a background sweep is a worse answer than skipping rows that cannot be compared.
    /// </summary>
    [Fact]
    public async Task rows_of_another_dimension_are_skipped_rather_than_raising()
    {
        await using DevBuddyDbContext context = _fixture.CreateContext();
        PostgresEmbeddingIndex index = new(context);
        ProjectScope scope = Scope();

        await index.UpsertAsync(
            scope,
            Model,
            [new EmbeddedRevision(KnowledgeRecordId.New(), 1, "h-768", Vector(1f, 0f, 0f))],
            CancellationToken.None);

        // Four dimensions against three stored. No hits, no exception.
        IReadOnlyList<SimilarRevision> hits = await index.FindSimilarAsync(
            scope, Model, Vector(1f, 0f, 0f, 0f), 10, CancellationToken.None);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task re_embedding_a_revision_replaces_its_row_rather_than_adding_one()
    {
        await using DevBuddyDbContext context = _fixture.CreateContext();
        PostgresEmbeddingIndex index = new(context);
        ProjectScope scope = Scope();
        KnowledgeRecordId record = KnowledgeRecordId.New();

        await index.UpsertAsync(
            scope, Model, [new EmbeddedRevision(record, 1, "first", Vector(1f, 0f, 0f))], CancellationToken.None);

        await index.UpsertAsync(
            scope, Model, [new EmbeddedRevision(record, 1, "second", Vector(0f, 1f, 0f))], CancellationToken.None);

        // One row, not two. Two would each rank separately and the same record would appear twice.
        SimilarRevision only = Assert.Single(await index.FindSimilarAsync(
            scope, Model, Vector(0f, 1f, 0f), 10, CancellationToken.None));

        Assert.Equal(record, only.RecordId);
        Assert.True(only.Distance < 0.001, $"The replacement vector should be the stored one, got {only.Distance}.");

        Assert.Equal(
            ["second"],
            await index.IndexedContentHashesAsync(scope, Model, CancellationToken.None));
    }

    [Fact]
    public async Task indexed_content_hashes_are_scoped_and_per_model()
    {
        await using DevBuddyDbContext context = _fixture.CreateContext();
        PostgresEmbeddingIndex index = new(context);
        ProjectScope mine = Scope();
        ProjectScope theirs = Scope();

        await index.UpsertAsync(
            mine, Model, [new EmbeddedRevision(KnowledgeRecordId.New(), 1, "mine", Vector(1f, 0f, 0f))], CancellationToken.None);

        await index.UpsertAsync(
            theirs, Model, [new EmbeddedRevision(KnowledgeRecordId.New(), 1, "theirs", Vector(1f, 0f, 0f))], CancellationToken.None);

        Assert.Equal(["mine"], await index.IndexedContentHashesAsync(mine, Model, CancellationToken.None));
        Assert.Empty(await index.IndexedContentHashesAsync(mine, "another-model", CancellationToken.None));
    }

    [Fact]
    public async Task purging_a_project_leaves_every_other_project_alone()
    {
        await using DevBuddyDbContext context = _fixture.CreateContext();
        PostgresEmbeddingIndex index = new(context);
        ProjectScope doomed = Scope();
        ProjectScope survivor = Scope();

        await index.UpsertAsync(
            doomed, Model, [new EmbeddedRevision(KnowledgeRecordId.New(), 1, "a", Vector(1f, 0f, 0f))], CancellationToken.None);

        await index.UpsertAsync(
            survivor, Model, [new EmbeddedRevision(KnowledgeRecordId.New(), 1, "b", Vector(1f, 0f, 0f))], CancellationToken.None);

        Assert.Equal(1, await index.PurgeProjectAsync(doomed, CancellationToken.None));

        Assert.Empty(await index.IndexedContentHashesAsync(doomed, Model, CancellationToken.None));
        Assert.Equal(["b"], await index.IndexedContentHashesAsync(survivor, Model, CancellationToken.None));
    }

    [Fact]
    public async Task an_empty_query_vector_has_no_nearest_neighbour()
    {
        await using DevBuddyDbContext context = _fixture.CreateContext();
        PostgresEmbeddingIndex index = new(context);
        ProjectScope scope = Scope();

        await index.UpsertAsync(
            scope, Model, [new EmbeddedRevision(KnowledgeRecordId.New(), 1, "a", Vector(1f, 0f, 0f))], CancellationToken.None);

        // A question with no content in it is answered as no hits, not as an error.
        Assert.Empty(await index.FindSimilarAsync(
            scope, Model, ReadOnlyMemory<float>.Empty, 10, CancellationToken.None));
    }

    [Fact]
    public async Task an_empty_upsert_writes_nothing()
    {
        await using DevBuddyDbContext context = _fixture.CreateContext();
        PostgresEmbeddingIndex index = new(context);

        Assert.Equal(0, await index.UpsertAsync(Scope(), Model, [], CancellationToken.None));
    }

    /// <summary>
    /// A nearest-neighbour query with no ceiling is a full scan with a sort. The store clamps
    /// whatever it is asked for, so a caller cannot turn a search into an export.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MaxValue)]
    public async Task the_result_count_is_bounded_whatever_is_asked_for(int limit)
    {
        await using DevBuddyDbContext context = _fixture.CreateContext();
        PostgresEmbeddingIndex index = new(context);
        ProjectScope scope = Scope();

        for (int number = 1; number <= 3; number++)
        {
            await index.UpsertAsync(
                scope,
                Model,
                [new EmbeddedRevision(KnowledgeRecordId.New(), number, $"h{number}", Vector(1f, 0f, 0f))],
                CancellationToken.None);
        }

        IReadOnlyList<SimilarRevision> hits = await index.FindSimilarAsync(
            scope, Model, Vector(1f, 0f, 0f), limit, CancellationToken.None);

        Assert.InRange(hits.Count, 1, 3);
    }

    private static ProjectScope Scope() =>
        new(new WorkspaceId(Guid.NewGuid()), new ProjectId(Guid.NewGuid()));

    private static ReadOnlyMemory<float> Vector(params float[] numbers) => numbers;
}
