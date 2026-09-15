using System.Text.Json;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;

namespace DevBuddy.Application.Tests;

/// <summary>
/// Whether an AI wrote a draft is decided by the channel the draft arrived on, never by what the
/// request says about itself.
/// <para>
/// Observed against v1.2.1 on 2026-09-15: a draft created over MCP naming
/// <c>RepositoryAnalysis</c> as its source came back as not AI-generated, was approved and
/// published through the web interface, and the approver was never shown that an AI wrote it.
/// </para>
/// </summary>
public sealed class DraftProvenanceTests
{
    [Theory]
    [InlineData(ProvenanceSourceKind.HumanAuthored)]
    [InlineData(ProvenanceSourceKind.RepositoryAnalysis)]
    [InlineData(ProvenanceSourceKind.GitHistory)]
    [InlineData(ProvenanceSourceKind.Document)]
    [InlineData(ProvenanceSourceKind.AiDraft)]
    public async Task a_draft_created_on_the_ai_channel_is_ai_generated_whatever_source_it_names(
        ProvenanceSourceKind claimed)
    {
        var harness = new Harness();

        await harness.SucceedAsync(
            new CreateDraftUseCase(harness.Ports, harness.Ports),
            new CreateDraftRequest(
                TestData.Scope, TestData.WorkItem, RecordKind.Decision,
                "Import order", "The importer normalises first.", Claiming(claimed)),
            TestData.Ai);

        Provenance stored = Assert.IsType<KnowledgeRecord>(harness.Ports.Saved).CurrentRevision.Provenance;

        Assert.True(stored.IsAiGenerated);

        // What it says it analysed is still recorded, as that and nothing more.
        Assert.Equal(claimed, stored.SourceKind);
    }

    [Theory]
    [InlineData(ProvenanceSourceKind.HumanAuthored, false)]
    [InlineData(ProvenanceSourceKind.RepositoryAnalysis, false)]
    [InlineData(ProvenanceSourceKind.AiDraft, true)]
    public async Task a_draft_a_person_creates_is_ai_generated_only_when_they_declare_it(
        ProvenanceSourceKind declared, bool expected)
    {
        var harness = new Harness();

        await harness.SucceedAsync(
            new CreateDraftUseCase(harness.Ports, harness.Ports),
            new CreateDraftRequest(
                TestData.Scope, TestData.WorkItem, RecordKind.Decision,
                "Import order", "The importer normalises first.", Claiming(declared)),
            TestData.Human);

        Provenance stored = Assert.IsType<KnowledgeRecord>(harness.Ports.Saved).CurrentRevision.Provenance;

        Assert.Equal(expected, stored.IsAiGenerated);
    }

    [Fact]
    public async Task a_person_revising_an_ai_draft_does_not_make_it_their_own_work()
    {
        var harness = new Harness();

        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), TestData.Scope, TestData.WorkItem, RecordKind.Decision,
            "Import order", "Written by an assistant.", null,
            Claiming(ProvenanceSourceKind.RepositoryAnalysis).ForCaller(TestData.Ai),
            TestData.Now, TestData.Author);

        harness.Ports.Record = record;

        await harness.SucceedAsync(
            new ReviseDraftUseCase(harness.Ports, harness.Ports),
            new ReviseDraftRequest(
                TestData.Scope, record.Id, "Import order", "Tidied by a person.", TestData.Draft),
            TestData.Human);

        Assert.Equal(2, record.CurrentRevision.Number);
        Assert.True(record.CurrentRevision.Provenance.IsAiGenerated);
    }

    [Fact]
    public void the_ai_channel_marks_provenance_even_for_an_operation_it_cannot_reach()
    {
        // revise_draft is human-only today. The rule is applied in the conversion rather than per
        // operation, so exposing another write later cannot quietly skip it.
        Assert.True(TestData.Draft.ForCaller(TestData.Ai).IsAiGenerated);
        Assert.False(TestData.Draft.ForCaller(TestData.Human).IsAiGenerated);
    }

    [Fact]
    public async Task provenance_the_domain_would_refuse_is_invalid_input_and_nothing_is_touched()
    {
        var harness = new Harness();

        UseCaseResult<LifecycleResult> result = await harness.RunAsync(
            new CreateDraftUseCase(harness.Ports, harness.Ports),
            new CreateDraftRequest(
                TestData.Scope, TestData.WorkItem, RecordKind.Decision, "Title", "Body",
                new DraftProvenance(ProvenanceSourceKind.HumanAuthored, " ", "Jennarin", TestData.Now)));

        Assert.Equal(ExecutionOutcome.Invalid, result.Outcome);
        Assert.Contains("sourceLocator", string.Join(" ", result.ValidationErrors), StringComparison.Ordinal);
        Assert.Null(harness.Ports.Saved);
    }

    /// <summary>
    /// The schema is what a model reads to decide what to send, so a field there is an invitation.
    /// Before the fix both requests offered <c>isAiGenerated</c> and then discarded it.
    /// </summary>
    [Theory]
    [InlineData(typeof(CreateDraftRequest))]
    [InlineData(typeof(ReviseDraftRequest))]
    public void a_draft_request_offers_no_field_for_saying_whether_an_ai_wrote_it(Type request)
    {
        JsonElement provenance = OperationSchemas.For(request)
            .GetProperty("properties")
            .GetProperty("provenance")
            .GetProperty("properties");

        // Checked first so the assertion below cannot pass against a schema that says nothing.
        Assert.True(provenance.TryGetProperty("sourceKind", out _));
        Assert.False(provenance.TryGetProperty("isAiGenerated", out _));
    }

    private static DraftProvenance Claiming(ProvenanceSourceKind kind) =>
        new(kind, "src/Importer.cs", "an assistant", TestData.Now);
}
