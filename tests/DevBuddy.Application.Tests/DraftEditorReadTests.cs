using DevBuddy.Application.Security;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;

namespace DevBuddy.Application.Tests;

/// <summary>
/// What a person editing a draft, or a reviewer approving one, has to be able to read.
/// <list type="bullet">
/// <item>The front matter, which the content hash covers.</item>
/// <item>The evidence a revision cites.</item>
/// <item>Why a reviewer sent it back.</item>
/// </list>
/// Until 2026-09-16, <c>get_record</c> returned neither of the first two and nothing returned the
/// third, so a revision written from what the web page could read silently dropped them.
/// </summary>
public sealed class DraftEditorReadTests
{
    private static readonly EvidenceObjectId RunLog = new(new Guid("99999999-0000-0000-0000-000000000001"));

    private static KnowledgeRecord DraftWith(string owner, string evidenceDescription) =>
        KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(),
            TestData.Scope,
            TestData.WorkItem,
            RecordKind.Decision,
            "Why the import runs first",
            "The importer normalises first.",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["owner"] = owner, ["area"] = "import" },
            new Provenance(
                ProvenanceSourceKind.HumanAuthored,
                "handover/2026-09-01",
                "Jennarin",
                TestData.Now,
                [new EvidenceReference(RunLog, evidenceDescription)]),
            TestData.Now,
            TestData.Author);

    private static async Task<KnowledgeRecordView> ReadAsync(KnowledgeRecord record, CallerContext caller)
    {
        var harness = new Harness();
        harness.Ports.Record = record;

        return await harness.SucceedAsync(
            new GetRecordUseCase(harness.Ports),
            new GetRecordRequest(TestData.Scope, record.Id, RevisionNumber: 1),
            caller);
    }

    [Fact]
    public async Task get_record_returns_the_front_matter_and_the_evidence_the_revision_cites()
    {
        KnowledgeRecordView view = await ReadAsync(DraftWith("Platform team", "Import run log"), TestData.Human);

        Assert.Equal("Platform team", view.FrontMatter["owner"]);
        Assert.Equal("import", view.FrontMatter["area"]);
        Assert.Equal(2, view.FrontMatter.Count);

        EvidenceReferenceView cited = Assert.Single(view.Evidence);
        Assert.Equal(RunLog, cited.EvidenceObjectId);
        Assert.Equal("Import run log", cited.Description);
        Assert.Equal(1, view.Provenance.EvidenceCount);
    }

    [Fact]
    public async Task on_the_ai_channel_front_matter_values_and_evidence_descriptions_are_redacted_but_keys_and_identifiers_are_not()
    {
        KnowledgeRecordView view = await ReadAsync(
            DraftWith("Owner reachable as PII", "Run log naming PII"), TestData.Ai);

        // SB-18: no bounded scope is approved, so personal data is redacted on the AI channel from
        // these fields as it is from the body.
        Assert.DoesNotContain("PII", view.FrontMatter["owner"], StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", view.FrontMatter["owner"], StringComparison.Ordinal);
        Assert.Equal("import", view.FrontMatter["area"]);

        EvidenceReferenceView cited = Assert.Single(view.Evidence);
        Assert.Equal(RunLog, cited.EvidenceObjectId);
        Assert.DoesNotContain("PII", cited.Description, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", cited.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_person_reads_personal_data_in_front_matter_but_never_a_secret()
    {
        KnowledgeRecordView view = await ReadAsync(
            DraftWith("Owner reachable as PII, key SECRET", "Run log with SECRET inside"), TestData.Human);

        // SB-18 does not apply to a person. SB-17 applies to everyone.
        Assert.Contains("PII", view.FrontMatter["owner"], StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", view.FrontMatter["owner"], StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", Assert.Single(view.Evidence).Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task record_history_carries_the_reason_a_reviewer_gave_and_redacts_it_like_other_text()
    {
        KnowledgeRecord record = TestData.NewDraft();
        record.SubmitForApproval(TestData.Now);
        record.RequestCorrection(TestData.Reviewer, "The rollback step is missing; ask PII.", TestData.Now);

        var harness = new Harness();
        harness.Ports.Record = record;
        var useCase = new ViewRecordHistoryUseCase(harness.Ports);
        var request = new ViewRecordHistoryRequest(TestData.Scope, record.Id);

        RecordHistoryResponse forPerson = await harness.SucceedAsync(useCase, request, TestData.Human);

        CorrectionView correction = Assert.Single(forPerson.Corrections);
        Assert.Equal("The rollback step is missing; ask PII.", correction.Reason);
        Assert.Equal(1, correction.TargetRevisionNumber);
        Assert.Equal(TestData.Reviewer, correction.RequestedBy);
        Assert.Equal(TestData.Now, correction.RequestedAt);

        RecordHistoryResponse forAssistant = await harness.SucceedAsync(useCase, request, TestData.Ai);

        string redacted = Assert.Single(forAssistant.Corrections).Reason;
        Assert.DoesNotContain("PII", redacted, StringComparison.Ordinal);
        Assert.Contains("The rollback step is missing", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_record_nobody_sent_back_has_no_corrections()
    {
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft();

        RecordHistoryResponse history = await harness.SucceedAsync(
            new ViewRecordHistoryUseCase(harness.Ports),
            new ViewRecordHistoryRequest(TestData.Scope, harness.Ports.Record.Id));

        Assert.Empty(history.Corrections);
    }
}
