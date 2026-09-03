using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Knowledge;

namespace DevBuddy.Application.Tests;

/// <summary>
/// SB-18 at the point where the pipeline decides, not where the scanner recognises a shape
/// (<c>PersonalDataCorpusTests</c> covers that). <see cref="FakePorts"/> treats the literal "PII"
/// as a finding and "SECRET" as one too, on the same convention the existing secret tests use, so
/// these tests are about the gating logic: which channel, and whether a bounded scope was
/// approved, not about regex correctness.
/// </summary>
public sealed class PersonalDataPolicyTests
{
    private static CreateDraftRequest DraftContaining(string body) =>
        new(TestData.Scope, TestData.WorkItem, RecordKind.Decision, "Title", body, TestData.Provenance);

    [Fact]
    public async Task the_ai_channel_with_no_bounded_scope_blocks_personal_data_in_a_draft()
    {
        var harness = new Harness();
        var useCase = new CreateDraftUseCase(harness.Ports, harness.Ports);

        UseCaseResult<LifecycleResult> result = await harness.RunAsync(
            useCase, DraftContaining("Contact on file: PII"), TestData.Ai);

        Assert.Equal(ExecutionOutcome.Blocked, result.Outcome);
        Assert.Contains(result.ValidationErrors, finding => finding.StartsWith("personal-data:", StringComparison.Ordinal));
        Assert.Null(harness.Ports.Saved);
    }

    [Fact]
    public async Task an_approved_bounded_scope_lets_personal_data_through_on_the_ai_channel()
    {
        var harness = new Harness();
        harness.Authorization.BoundedDataScope = "support-escalations";
        var useCase = new CreateDraftUseCase(harness.Ports, harness.Ports);

        LifecycleResult result = await harness.SucceedAsync(
            useCase, DraftContaining("Contact on file: PII"), TestData.Ai);

        Assert.NotNull(harness.Ports.Saved);
        _ = result;
    }

    [Fact]
    public async Task the_human_channel_is_never_subject_to_the_personal_data_control()
    {
        var harness = new Harness();
        var useCase = new CreateDraftUseCase(harness.Ports, harness.Ports);

        // No bounded scope approved, and the content still carries the PII marker: a human
        // working their own project is ordinary use, which SB-18 has nothing to say about.
        await harness.SucceedAsync(useCase, DraftContaining("Contact on file: PII"), TestData.Human);

        Assert.NotNull(harness.Ports.Saved);
    }

    [Fact]
    public async Task a_bounded_scope_does_not_excuse_a_secret()
    {
        var harness = new Harness();
        harness.Authorization.BoundedDataScope = "support-escalations";
        var useCase = new CreateDraftUseCase(harness.Ports, harness.Ports);

        UseCaseResult<LifecycleResult> result = await harness.RunAsync(
            useCase, DraftContaining("api key: SECRET"), TestData.Ai);

        // The bounded scope is a personal-data exception. The prohibition on secrets, per
        // info.md, still applies inside it.
        Assert.Equal(ExecutionOutcome.Blocked, result.Outcome);
        Assert.DoesNotContain(result.ValidationErrors, finding => finding.StartsWith("personal-data:", StringComparison.Ordinal));
        Assert.Null(harness.Ports.Saved);
    }

    [Fact]
    public async Task a_read_on_the_ai_channel_with_no_bounded_scope_has_personal_data_redacted()
    {
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft("Body naming PII directly.");
        var useCase = new GetRecordUseCase(harness.Ports);

        KnowledgeRecordView view = await harness.SucceedAsync(
            useCase, new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id), TestData.Ai);

        Assert.DoesNotContain("PII", view.Body, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", view.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_read_on_the_ai_channel_with_an_approved_bounded_scope_is_not_redacted()
    {
        var harness = new Harness();
        harness.Authorization.BoundedDataScope = "support-escalations";
        harness.Ports.Record = TestData.NewDraft("Body naming PII directly.");
        var useCase = new GetRecordUseCase(harness.Ports);

        KnowledgeRecordView view = await harness.SucceedAsync(
            useCase, new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id), TestData.Ai);

        Assert.Contains("PII", view.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_read_on_the_human_channel_is_never_redacted_for_personal_data()
    {
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft("Body naming PII directly.");
        var useCase = new GetRecordUseCase(harness.Ports);

        KnowledgeRecordView view = await harness.SucceedAsync(
            useCase, new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id), TestData.Human);

        Assert.Contains("PII", view.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_secret_is_still_redacted_from_an_ai_read_inside_a_bounded_scope()
    {
        var harness = new Harness();
        harness.Authorization.BoundedDataScope = "support-escalations";
        harness.Ports.Record = TestData.NewDraft("Body naming SECRET directly.");
        var useCase = new GetRecordUseCase(harness.Ports);

        KnowledgeRecordView view = await harness.SucceedAsync(
            useCase, new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id), TestData.Ai);

        // The bounded scope excuses personal data, not secrets: SB-17 has no exception.
        Assert.DoesNotContain("SECRET", view.Body, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", view.Body, StringComparison.Ordinal);
    }
}
