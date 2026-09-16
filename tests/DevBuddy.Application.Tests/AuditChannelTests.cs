using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Knowledge;

namespace DevBuddy.Application.Tests;

/// <summary>
/// Every audit entry the pipeline writes records the channel the request arrived on.
/// <para>
/// The actor alone cannot say it. A machine token's owner is an ordinary user, so an assistant's
/// call through that token and the same person's own call left identical rows, and on 2026-09-15
/// drafts created over the AI channel could not be told apart from typed ones afterwards.
/// </para>
/// </summary>
public sealed class AuditChannelTests
{
    public static TheoryData<AccessChannel> EveryAccessChannel()
    {
        var data = new TheoryData<AccessChannel>();

        foreach (AccessChannel channel in Enum.GetValues<AccessChannel>())
        {
            data.Add(channel);
        }

        return data;
    }

    /// <summary>
    /// Driven by the enum rather than by a list, so a channel added later arrives here without
    /// anybody remembering to add it, and fails until the audit trail can record it.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryAccessChannel))]
    public async Task a_successful_call_is_audited_with_the_channel_it_arrived_on(AccessChannel channel)
    {
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft();

        await harness.SucceedAsync(
            new GetRecordUseCase(harness.Ports),
            new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id),
            new CallerContext(TestData.Author, channel, $"req-{channel}"));

        AuditEvent entry = Assert.Single(harness.Audit.Entries);

        // Matched by name, independently of the executor's own mapping, and by number, because
        // the audit enum promises the stored value means the same as the access channel's.
        Assert.Equal(Enum.Parse<AuditChannel>(channel.ToString()), entry.Channel);
        Assert.Equal((int)channel, (int?)entry.Channel);
    }

    [Fact]
    public async Task a_structural_denial_on_the_ai_channel_is_audited_as_ai()
    {
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft();

        UseCaseResult<LifecycleResult> result = await harness.RunAsync(
            new PublishRecordUseCase(harness.Ports, harness.Ports),
            new RecordActionRequest(TestData.Scope, harness.Ports.Record.Id),
            TestData.Ai);

        Assert.Equal(ExecutionOutcome.Denied, result.Outcome);

        AuditEvent entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditAction.AccessDenied, entry.Action);
        Assert.Equal(AuditChannel.Ai, entry.Channel);
    }

    [Fact]
    public async Task blocked_content_on_the_ai_channel_is_audited_as_ai()
    {
        var harness = new Harness();

        UseCaseResult<LifecycleResult> result = await harness.RunAsync(
            new CreateDraftUseCase(harness.Ports, harness.Ports),
            new CreateDraftRequest(
                TestData.Scope, TestData.WorkItem, RecordKind.Decision, "Title", "api key: SECRET", TestData.Draft),
            TestData.Ai);

        Assert.Equal(ExecutionOutcome.Blocked, result.Outcome);

        AuditEvent entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditAction.ContentScanned, entry.Action);
        Assert.Equal(AuditChannel.Ai, entry.Channel);
    }

    [Fact]
    public async Task a_rejected_domain_rule_from_a_person_is_audited_as_human()
    {
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft();

        UseCaseResult<LifecycleResult> result = await harness.RunAsync(
            new PublishRecordUseCase(harness.Ports, harness.Ports),
            new RecordActionRequest(TestData.Scope, harness.Ports.Record.Id),
            TestData.Human);

        Assert.Equal(ExecutionOutcome.Rejected, result.Outcome);

        AuditEvent entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal(AuditChannel.Human, entry.Channel);
    }

    [Fact]
    public async Task an_audit_read_filtered_on_a_channel_nobody_records_is_refused()
    {
        var harness = new Harness();

        UseCaseResult<AuditHistoryResponse> result = await harness.RunAsync(
            new ReadAuditHistoryUseCase(harness.Ports),
            new ReadAuditHistoryRequest(
                TestData.Scope, TestData.Now, TestData.Now.AddDays(1), Channel: (AuditChannel)9));

        // Otherwise a number no channel carries would answer "nothing happened on it".
        Assert.Equal(ExecutionOutcome.Invalid, result.Outcome);
        Assert.Empty(harness.Audit.Entries);
    }
}
