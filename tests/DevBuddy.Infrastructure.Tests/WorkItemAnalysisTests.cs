using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Analysis;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// <c>analyze_work_items</c> over the real repository.
/// <para>
/// Until 2026-09-15 this analysis asked for the records of the empty work item identifier and
/// reported zero for every project, while <c>get_work_item</c> on the same installation counted
/// the records correctly. A fake repository would have answered whatever the analyser asked, which
/// is how nobody noticed, so this runs against PostgreSQL.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WorkItemAnalysisTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task work_items_are_counted_with_their_records_and_their_gaps()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        var reviewer = UserId.New();

        // Exclusions recorded; one published record and one draft.
        WorkItem documented = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "ALPHA-1");

        // No exclusions; one record, awaiting approval and never published.
        WorkItem unscoped = seed.NewWorkItem(seed.Alpha, "ALPHA-2");

        // Exclusions recorded; nothing written against it at all.
        WorkItem empty = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "ALPHA-3");

        // Another project, with a published record. It must not be counted.
        WorkItem elsewhere = await seed.AddWorkItemAsync(_fixture, seed.Beta, "BETA-1");

        await using (DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace))
        {
            var repository = new KnowledgeRepository(context);
            await repository.AddWorkItemAsync(unscoped, Ct);

            await repository.AddRecordAsync(
                seed.NewPublished(seed.Alpha, documented.Id, "Why the import runs first", "It normalises.", reviewer), Ct);
            await repository.AddRecordAsync(
                seed.NewDraft(seed.Alpha, documented.Id, "Rollback", "Not written yet."), Ct);

            KnowledgeRecord pending = seed.NewDraft(seed.Alpha, unscoped.Id, "Retry policy", "Three attempts.");
            pending.SubmitForApproval(Seed.Now.AddMinutes(1));
            await repository.AddRecordAsync(pending, Ct);

            await repository.AddRecordAsync(
                seed.NewPublished(seed.Beta, elsewhere.Id, "Beta decision", "Beta only.", reviewer), Ct);
        }

        AnalysisReport report;

        await using (DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace))
        {
            var analyzer = new FileSystemCodeAnalyzer(
                new KnowledgeRepository(context), Options.Create(new AnalysisOptions()));

            report = await analyzer.AnalyzeAsync(AnalysisKind.WorkItems, seed.Alpha, null, null, Ct);
        }

        Assert.Equal(AnalysisKind.WorkItems, report.Kind);
        Assert.Equal(
            "3 work items in this project: 2 with knowledge records, 1 with published knowledge, "
            + "1 without recorded exclusions.",
            report.Summary);

        (string Subject, string Detail, WorkItemId WorkItem)[] expected =
            [
                ("work-item", "ALPHA-1: 2 knowledge records, 1 published", documented.Id),
                ("work-item", "ALPHA-2: 1 knowledge record, 0 published", unscoped.Id),
                ("no-published-knowledge", "Work item ALPHA-2 has 1 knowledge record and none is published.", unscoped.Id),
                ("no-exclusions", "Work item ALPHA-2 does not record what is out of scope.", unscoped.Id),
                ("work-item", "ALPHA-3: 0 knowledge records, 0 published", empty.Id),
                ("no-published-knowledge", "Work item ALPHA-3 has no knowledge records.", empty.Id),
            ];

        // Every observation, in order, and nothing from the other project: BETA-1 is absent.
        Assert.Equal(
            expected,
            report.Observations.Select(observation => (
                observation.Subject,
                observation.Detail,
                new WorkItemId(Guid.Parse(observation.SourceLocator)))));
    }
}
