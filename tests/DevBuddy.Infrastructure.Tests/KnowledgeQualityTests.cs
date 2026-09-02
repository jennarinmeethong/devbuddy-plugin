using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Analysis;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The knowledge-quality sweeps against real PostgreSQL.
/// <para>
/// All three report and none repair. A sweep that rewrote provenance or merged two records would
/// destroy the traceability the system exists to provide, and it would do it invisibly.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class KnowledgeQualityTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task provenance_validation_reports_a_record_that_cites_no_evidence()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "QLT-1");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        KnowledgeRecord record = seed.NewDraft(seed.Alpha, item.Id, "Title", "Body.");
        await new KnowledgeRepository(context).AddRecordAsync(record, Ct);

        IReadOnlyList<QualityFinding> findings =
            await new KnowledgeQualityChecks(context).ValidateProvenanceAsync(seed.Alpha, Ct);

        Assert.Contains(findings, finding =>
            finding.RecordId == record.Id && finding.Rule == "no-evidence");
    }

    [Fact]
    public async Task a_record_with_evidence_is_not_reported()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "QLT-2");

        var provenance = new Provenance(
            ProvenanceSourceKind.TestEvidence,
            "ci/run/8421",
            "pipeline",
            Seed.Now,
            [new EvidenceReference(EvidenceObjectId.New(), "The failing test output.")]);

        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), seed.Alpha, item.Id, RecordKind.DeliveryState,
            "Verified", "The migration ran green.", null, provenance, Seed.Now, seed.Author);

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        await new KnowledgeRepository(context).AddRecordAsync(record, Ct);

        IReadOnlyList<QualityFinding> findings =
            await new KnowledgeQualityChecks(context).ValidateProvenanceAsync(seed.Alpha, Ct);

        Assert.DoesNotContain(findings, finding => finding.RecordId == record.Id);
    }

    [Fact]
    public async Task an_unpublished_ai_draft_is_reported_for_review()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "QLT-3");

        var provenance = new Provenance(
            ProvenanceSourceKind.AiDraft, "mcp/create_draft", "Claude", Seed.Now,
            [new EvidenceReference(EvidenceObjectId.New(), "Analysis output.")]);

        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), seed.Alpha, item.Id, RecordKind.Decision,
            "Drafted by AI", "Body.", null, provenance, Seed.Now, seed.Author);

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        await new KnowledgeRepository(context).AddRecordAsync(record, Ct);

        IReadOnlyList<QualityFinding> findings =
            await new KnowledgeQualityChecks(context).ValidateProvenanceAsync(seed.Alpha, Ct);

        // AI may draft, and a human decides. An AI draft nobody ever looked at is exactly the
        // thing that should not quietly become part of the record.
        Assert.Contains(findings, finding =>
            finding.RecordId == record.Id && finding.Rule == "unreviewed-ai-draft");
    }

    [Fact]
    public async Task duplicate_detection_finds_two_records_with_identical_content()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "QLT-4");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var repository = new KnowledgeRepository(context);

        KnowledgeRecord first = seed.NewDraft(seed.Alpha, item.Id, "Same", "Exactly the same text.");
        KnowledgeRecord second = seed.NewDraft(seed.Alpha, item.Id, "Same", "Exactly the same text.");
        KnowledgeRecord other = seed.NewDraft(seed.Alpha, item.Id, "Different", "Different text.");

        await repository.AddRecordAsync(first, Ct);
        await repository.AddRecordAsync(second, Ct);
        await repository.AddRecordAsync(other, Ct);

        IReadOnlyList<QualityFinding> findings =
            await new KnowledgeQualityChecks(context).DetectDuplicatesAsync(seed.Alpha, Ct);

        // Both sides are reported, because a human deciding which to keep needs to see both.
        Assert.Contains(findings, finding => finding.RecordId == first.Id);
        Assert.Contains(findings, finding => finding.RecordId == second.Id);
        Assert.DoesNotContain(findings, finding => finding.RecordId == other.Id);
    }

    [Fact]
    public async Task duplicate_detection_does_not_look_across_a_project_boundary()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem alphaItem = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "QLT-5A");
        WorkItem betaItem = await seed.AddWorkItemAsync(_fixture, seed.Beta, "QLT-5B");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var repository = new KnowledgeRepository(context);

        await repository.AddRecordAsync(
            seed.NewDraft(seed.Alpha, alphaItem.Id, "Shared", "Identical across projects."), Ct);

        await repository.AddRecordAsync(
            seed.NewDraft(seed.Beta, betaItem.Id, "Shared", "Identical across projects."), Ct);

        IReadOnlyList<QualityFinding> findings =
            await new KnowledgeQualityChecks(context).DetectDuplicatesAsync(seed.Alpha, Ct);

        // Two projects writing the same sentence is not a duplicate, and reporting it would leak
        // that the other project exists (SB-12).
        Assert.Empty(findings);
    }

    [Fact]
    public async Task staleness_reports_old_records_and_leaves_recent_ones_alone()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "QLT-6");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        KnowledgeRecord old = seed.NewDraft(seed.Alpha, item.Id, "Old", "Written long ago.");
        await new KnowledgeRepository(context).AddRecordAsync(old, Ct);

        var checks = new KnowledgeQualityChecks(context);

        Assert.Contains(
            await checks.DetectStalenessAsync(seed.Alpha, Seed.Now.AddDays(1), Ct),
            finding => finding.RecordId == old.Id);

        Assert.Empty(await checks.DetectStalenessAsync(seed.Alpha, Seed.Now.AddDays(-1), Ct));
    }

    [Fact]
    public async Task an_archived_record_is_not_swept()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "QLT-7");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var repository = new KnowledgeRepository(context);

        KnowledgeRecord record = seed.NewDraft(seed.Alpha, item.Id, "Retired", "No longer relevant.");
        await repository.AddRecordAsync(record, Ct);

        record.Archive(Seed.Now.AddMinutes(5));
        await repository.UpdateRecordAsync(record, Ct);

        var checks = new KnowledgeQualityChecks(context);

        // Archiving is the answer to a stale record. Reporting it again afterwards would train
        // people to ignore the sweep.
        Assert.Empty(await checks.DetectStalenessAsync(seed.Alpha, Seed.Now.AddDays(1), Ct));
        Assert.DoesNotContain(
            await checks.ValidateProvenanceAsync(seed.Alpha, Ct),
            finding => finding.RecordId == record.Id);
    }

    [Fact]
    public async Task the_sweeps_look_at_the_published_revision_rather_than_a_later_draft()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "QLT-8");
        var reviewer = UserId.New();

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var repository = new KnowledgeRepository(context);

        var cited = new Provenance(
            ProvenanceSourceKind.TestEvidence, "ci/run/9001", "pipeline", Seed.Now,
            [new EvidenceReference(EvidenceObjectId.New(), "The passing run.")]);

        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), seed.Alpha, item.Id, RecordKind.DeliveryState,
            "Published", "Approved and cited.", null, cited, Seed.Now, seed.Author);

        record.SubmitForApproval(Seed.Now.AddMinutes(1));
        record.Approve(reviewer, record.CurrentRevision.ContentHash, Seed.Now.AddMinutes(2));
        record.Publish(Seed.Now.AddMinutes(3));

        await repository.AddRecordAsync(record, Ct);

        // A later draft that cites nothing. The published revision does cite evidence.
        record.AddRevision(
            "Published",
            "A later draft with no evidence.",
            null,
            new Provenance(ProvenanceSourceKind.HumanAuthored, "draft-in-progress", "Jennarin", Seed.Now),
            Seed.Now.AddMinutes(30),
            seed.Author);

        await repository.UpdateRecordAsync(record, Ct);

        IReadOnlyList<QualityFinding> findings =
            await new KnowledgeQualityChecks(context).ValidateProvenanceAsync(seed.Alpha, Ct);

        // The published revision is what readers see, so it is what the sweep judges. Reporting an
        // unfinished draft would bury the findings that matter.
        Assert.DoesNotContain(findings, finding =>
            finding.RecordId == record.Id && finding.Rule == "no-evidence");
    }

    [Fact]
    public async Task a_row_written_around_the_domain_with_no_source_locator_is_still_reported()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "QLT-9");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        KnowledgeRecord record = seed.NewDraft(seed.Alpha, item.Id, "Imported", "From somewhere else.");
        await new KnowledgeRepository(context).AddRecordAsync(record, Ct);

        // The domain refuses a blank source locator, so this can only arrive by a route that went
        // around it: a bad import, a migration, a hand-run update. That is exactly the drift a
        // provenance sweep exists to surface.
        string blanked = """{"sourceKind":1,"sourceLocator":"","author":"x","recordedAt":"2026-09-01T09:00:00+00:00","evidence":[]}""";

        await context.Database.ExecuteSqlAsync(
            $"update record_revisions set provenance = CAST({blanked} AS jsonb) where record_id = {record.Id.Value}");

        IReadOnlyList<QualityFinding> findings =
            await new KnowledgeQualityChecks(context).ValidateProvenanceAsync(seed.Alpha, Ct);

        Assert.Contains(findings, finding =>
            finding.RecordId == record.Id && finding.Rule == "no-source-locator");
    }
}
