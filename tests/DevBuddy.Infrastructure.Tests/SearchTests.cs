using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Repositories;
using DevBuddy.Infrastructure.Persistence.Search;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// PostgreSQL full-text search over the generated tsvector column, with structured filters.
/// <para>
/// These run against real PostgreSQL because there is no other way to test them: the search
/// vector is a stored generated column and the query goes through a GIN index. Neither exists on
/// any substitute provider.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SearchTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task full_text_search_finds_a_record_by_a_word_in_its_body()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "SRCH-1");

        await AddAsync(seed, seed.Alpha, item.Id, "Import ordering",
            "The importer normalises identifiers before the validator sees them.");

        await AddAsync(seed, seed.Alpha, item.Id, "Unrelated note",
            "The nightly report is generated from the warehouse.");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var index = new PostgresSearchIndex(context);

        IReadOnlyList<KnowledgeSearchHit> hits = await index.SearchAsync(
            new KnowledgeSearchCriteria(seed.Alpha, "normalises identifiers"), Ct);

        KnowledgeSearchHit hit = Assert.Single(hits);
        Assert.Equal("Import ordering", hit.Title);
        Assert.True(hit.Rank > 0);
        Assert.Contains("normalises", hit.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task search_stems_words_the_way_postgresql_does()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "SRCH-2");

        await AddAsync(seed, seed.Alpha, item.Id, "Migration plan",
            "We migrated the importer in three steps.");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);

        // English stemming is the reason to use a tsvector rather than LIKE: "migrating" finds
        // "migrated". It is also the reason a semantic match is still out of reach without
        // embeddings, which v1 does not have.
        IReadOnlyList<KnowledgeSearchHit> hits = await new PostgresSearchIndex(context)
            .SearchAsync(new KnowledgeSearchCriteria(seed.Alpha, "migrating"), Ct);

        Assert.Single(hits);
    }

    [Fact]
    public async Task a_record_in_another_project_never_appears_in_a_search()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem alphaItem = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "SRCH-3A");
        WorkItem betaItem = await seed.AddWorkItemAsync(_fixture, seed.Beta, "SRCH-3B");

        await AddAsync(seed, seed.Alpha, alphaItem.Id, "Alpha decision",
            "The distinctive keyword is xylophone.");

        await AddAsync(seed, seed.Beta, betaItem.Id, "Beta decision",
            "The distinctive keyword is xylophone.");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var index = new PostgresSearchIndex(context);

        IReadOnlyList<KnowledgeSearchHit> fromAlpha = await index.SearchAsync(
            new KnowledgeSearchCriteria(seed.Alpha, "xylophone"), Ct);

        IReadOnlyList<KnowledgeSearchHit> fromBeta = await index.SearchAsync(
            new KnowledgeSearchCriteria(seed.Beta, "xylophone"), Ct);

        // Identical content in two projects. This is the case that catches a search that scopes
        // records but forgets to scope the revisions it joins to (SB-12).
        Assert.Equal("Alpha decision", Assert.Single(fromAlpha).Title);
        Assert.Equal("Beta decision", Assert.Single(fromBeta).Title);
    }

    [Fact]
    public async Task search_returns_nothing_when_no_workspace_is_set()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "SRCH-4");

        await AddAsync(seed, seed.Alpha, item.Id, "Scoped", "The keyword is kaleidoscope.");

        await using DevBuddyDbContext unscoped = _fixture.CreateContext(workspaceId: null);

        Assert.Empty(await new PostgresSearchIndex(unscoped)
            .SearchAsync(new KnowledgeSearchCriteria(seed.Alpha, "kaleidoscope"), Ct));
    }

    [Fact]
    public async Task structured_filters_narrow_the_result_by_kind_and_status()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "SRCH-5");
        var reviewer = UserId.New();

        KnowledgeRecord draft = seed.NewDraft(seed.Alpha, item.Id, "Draft note", "The keyword is quasar.");
        KnowledgeRecord published = seed.NewPublished(
            seed.Alpha, item.Id, "Published note", "The keyword is quasar.", reviewer);

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var repository = new KnowledgeRepository(context);
        await repository.AddRecordAsync(draft, Ct);
        await repository.AddRecordAsync(published, Ct);

        var index = new PostgresSearchIndex(context);

        Assert.Equal(2, (await index.SearchAsync(
            new KnowledgeSearchCriteria(seed.Alpha, "quasar"), Ct)).Count);

        IReadOnlyList<KnowledgeSearchHit> publishedOnly = await index.SearchAsync(
            new KnowledgeSearchCriteria(seed.Alpha, "quasar", Statuses: [RecordStatus.Published]), Ct);

        Assert.Equal("Published note", Assert.Single(publishedOnly).Title);

        Assert.Empty(await index.SearchAsync(
            new KnowledgeSearchCriteria(seed.Alpha, "quasar", Kinds: [RecordKind.Handover]), Ct));
    }

    [Fact]
    public async Task a_published_record_is_matched_on_its_published_revision_not_a_later_draft()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "SRCH-6");
        var reviewer = UserId.New();

        KnowledgeRecord record = seed.NewPublished(
            seed.Alpha, item.Id, "Published note", "The published keyword is heliotrope.", reviewer);

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var repository = new KnowledgeRepository(context);
        await repository.AddRecordAsync(record, Ct);

        record.AddRevision(
            "Published note",
            "The unapproved keyword is bellwether.",
            null,
            new Provenance(ProvenanceSourceKind.HumanAuthored, "review", "Jennarin", Seed.Now),
            Seed.Now.AddMinutes(20),
            seed.Author);

        await repository.UpdateRecordAsync(record, Ct);

        var index = new PostgresSearchIndex(context);

        // Searching finds what was published. Text that nobody has approved is not searchable
        // knowledge yet, however recently it was typed (SB-26).
        Assert.Single(await index.SearchAsync(
            new KnowledgeSearchCriteria(seed.Alpha, "heliotrope"), Ct));

        Assert.Empty(await index.SearchAsync(
            new KnowledgeSearchCriteria(seed.Alpha, "bellwether"), Ct));
    }

    [Fact]
    public async Task reindex_reports_the_revisions_it_covers_for_one_project_only()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem alphaItem = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "SRCH-7A");
        WorkItem betaItem = await seed.AddWorkItemAsync(_fixture, seed.Beta, "SRCH-7B");

        await AddAsync(seed, seed.Alpha, alphaItem.Id, "One", "First.");
        await AddAsync(seed, seed.Alpha, alphaItem.Id, "Two", "Second.");
        await AddAsync(seed, seed.Beta, betaItem.Id, "Three", "Third.");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var index = new PostgresSearchIndex(context);

        Assert.Equal(2, await index.ReindexAsync(seed.Alpha, Ct));
        Assert.Equal(1, await index.ReindexAsync(seed.Beta, Ct));
    }

    private async Task AddAsync(
        Seed seed, Domain.Tenancy.ProjectScope scope, WorkItemId workItemId, string title, string body)
    {
        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        await new KnowledgeRepository(context)
            .AddRecordAsync(seed.NewDraft(scope, workItemId, title, body), Ct);
    }
}
