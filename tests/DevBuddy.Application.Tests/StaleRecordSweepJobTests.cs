using System.Text.Json;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.Workers;
using DevBuddy.Application.Workers.Jobs;
using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The first worker job, which is a caller-bound sweep that touches no model.
/// <para>
/// What is worth testing here is not that it counts findings — <c>detect_staleness</c> has done
/// that since Phase 6 and this job reimplements none of it. What is worth testing is that it goes
/// through the dispatcher on every call, that it carries the caller it was given rather than one
/// of its own, and that a refusal is reported as information instead of being swallowed or thrown.
/// </para>
/// <para>
/// The dispatcher is real. Only the bindings behind it are fakes, so the AI-channel refusal in
/// <c>OperationDispatcher.InvokeAsync</c> is exercised rather than mocked away — which is what
/// makes the channel test below mean something.
/// </para>
/// </summary>
public sealed class StaleRecordSweepJobTests
{
    [Fact]
    public async Task the_sweep_asks_about_every_project_it_can_see()
    {
        RecordingDispatcher recorder = new();
        recorder.Answer("list_projects", Payload(new { projects = new[] { new { projectId = ProjectA }, new { projectId = ProjectB } } }));
        recorder.Answer("detect_staleness", Payload(new { findings = new[] { new { note = "old" }, new { note = "older" } } }));

        StaleRecordSweepJob job = new(recorder.Build(), TimeSpan.FromDays(90));

        WorkerRunReport report = await job.RunAsync(
            Caller(job.ModelUse), new WorkerBudget(0), CancellationToken.None);

        Assert.False(report.Refused);
        Assert.Equal("stale-record-sweep", report.JobName);

        Assert.Equal(
            ["list_projects", "detect_staleness", "detect_staleness"],
            recorder.Invoked);

        Assert.Equal(2, job.Findings.Count);
        Assert.All(job.Findings, finding => Assert.Equal(2, finding.StaleRecords));
        Assert.All(job.Findings, finding => Assert.Null(finding.Refusal));
    }

    /// <summary>
    /// The sweep costs nothing, and says so rather than omitting the figure: a reader comparing
    /// jobs should be able to see which ones spend money and which do not.
    /// </summary>
    [Fact]
    public async Task the_sweep_spends_nothing_because_it_calls_no_model()
    {
        RecordingDispatcher recorder = new();
        recorder.Answer("list_projects", Payload(new { projects = new[] { new { projectId = ProjectA } } }));
        recorder.Answer("detect_staleness", Payload(new { findings = Array.Empty<object>() }));

        WorkerBudget budget = new(5);
        StaleRecordSweepJob job = new(recorder.Build(), TimeSpan.FromDays(30));

        WorkerRunReport report = await job.RunAsync(Caller(job.ModelUse), budget, CancellationToken.None);

        Assert.Equal(0, report.CallsSpent);
        Assert.Equal(5, budget.Remaining);
    }

    /// <summary>
    /// The whole reason this job declares <see cref="WorkerModelUse.None"/>. On the AI channel the
    /// dispatcher refuses <c>detect_staleness</c> outright, because that channel is an allow-list
    /// of eighteen operations and this is not one of them — so a worker that had been pinned to
    /// the AI channel could not have performed this sweep at all.
    /// </summary>
    [Fact]
    public async Task the_same_sweep_would_be_refused_on_the_ai_channel()
    {
        RecordingDispatcher recorder = new();
        recorder.Answer("list_projects", Payload(new { projects = new[] { new { projectId = ProjectA } } }));
        recorder.Answer("detect_staleness", Payload(new { findings = Array.Empty<object>() }));

        StaleRecordSweepJob job = new(recorder.Build(), TimeSpan.FromDays(30));

        // Declared model use says InternalSystem; this deliberately runs it as an AI caller to
        // show what the declaration buys.
        WorkerRunReport onAi = await job.RunAsync(
            Caller(WorkerModelUse.SendsContentToAModel), new WorkerBudget(0), CancellationToken.None);

        Assert.False(onAi.Refused);
        StaleProjectSummary summary = Assert.Single(job.Findings);
        Assert.NotNull(summary.Refusal);
        Assert.Contains("No operation named detect_staleness", summary.Refusal, StringComparison.Ordinal);
        Assert.Equal(0, summary.StaleRecords);

        // Refused before the binding was even reached: the dispatcher stops an AI caller at the
        // allow-list rather than letting the use case decide.
        Assert.Equal(["list_projects"], recorder.Invoked);
    }

    /// <summary>
    /// A membership that reaches nothing is an answer, not a crash. It tells an operator the token
    /// is narrower than the sweep they asked for, which is the thing they need to know.
    /// </summary>
    [Fact]
    public async Task a_workspace_the_credential_cannot_list_is_reported_as_a_refusal()
    {
        RecordingDispatcher recorder = new();
        recorder.Refuse("list_projects", "The caller has no access to this scope.");

        StaleRecordSweepJob job = new(recorder.Build(), TimeSpan.FromDays(30));

        WorkerRunReport report = await job.RunAsync(
            Caller(job.ModelUse), new WorkerBudget(0), CancellationToken.None);

        Assert.True(report.Refused);
        Assert.Contains("no access", report.Reason!, StringComparison.Ordinal);
        Assert.Empty(job.Findings);
    }

    [Fact]
    public async Task a_project_the_credential_cannot_sweep_is_reported_per_project()
    {
        RecordingDispatcher recorder = new();
        recorder.Answer("list_projects", Payload(new { projects = new[] { new { projectId = ProjectA }, new { projectId = ProjectB } } }));
        recorder.Refuse("detect_staleness", "The role Viewer does not carry ManageIndex.");

        StaleRecordSweepJob job = new(recorder.Build(), TimeSpan.FromDays(30));

        WorkerRunReport report = await job.RunAsync(
            Caller(job.ModelUse), new WorkerBudget(0), CancellationToken.None);

        // The run completed. Every project refused, and the report says which and why.
        Assert.False(report.Refused);
        Assert.Equal(2, job.Findings.Count);
        Assert.All(job.Findings, finding => Assert.Contains("ManageIndex", finding.Refusal!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task a_workspace_with_no_projects_completes_without_asking_anything_else()
    {
        RecordingDispatcher recorder = new();
        recorder.Answer("list_projects", Payload(new { projects = Array.Empty<object>() }));

        StaleRecordSweepJob job = new(recorder.Build(), TimeSpan.FromDays(30));

        WorkerRunReport report = await job.RunAsync(
            Caller(job.ModelUse), new WorkerBudget(0), CancellationToken.None);

        Assert.False(report.Refused);
        Assert.Equal(["list_projects"], recorder.Invoked);
    }

    [Fact]
    public void the_sweep_declares_that_it_touches_no_model()
    {
        StaleRecordSweepJob job = new(new RecordingDispatcher().Build(), TimeSpan.FromDays(30));

        Assert.Equal(WorkerModelUse.None, job.ModelUse);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void a_staleness_window_that_is_not_a_duration_is_refused(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StaleRecordSweepJob(new RecordingDispatcher().Build(), TimeSpan.FromDays(days)));
    }

    private static readonly Guid ProjectA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static WorkerCaller Caller(WorkerModelUse modelUse) =>
        WorkerCaller.FromMachineToken(
            new Abstractions.MachineTokenIdentity(
                new MachineTokenId(Guid.NewGuid()),
                new UserId(Guid.NewGuid()),
                new WorkspaceId(Guid.NewGuid()),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(7)),
            "run-1",
            modelUse)!;

    private static JsonElement Payload(object body) =>
        JsonSerializer.SerializeToElement(body, JsonConventions.Options);

    /// <summary>
    /// A real dispatcher over fake bindings, so the dispatcher's own AI-channel refusal still
    /// applies. A fake dispatcher would have made the channel test prove nothing.
    /// </summary>
    private sealed class RecordingDispatcher
    {
        private readonly List<OperationBinding> _bindings = [];

        public List<string> Invoked { get; } = [];

        public void Answer(string operation, JsonElement payload) =>
            Add(operation, Descriptor(operation), (_, _, _) => Task.FromResult(
                new DispatchResult(operation, ExecutionOutcome.Succeeded, payload, string.Empty, [])));

        public void Refuse(string operation, string reason) =>
            Add(operation, Descriptor(operation), (_, _, _) => Task.FromResult(
                new DispatchResult(operation, ExecutionOutcome.Denied, null, reason, [])));

        public OperationDispatcher Build() => new(_bindings);

        private void Add(
            string operation,
            UseCaseDescriptor descriptor,
            Func<JsonElement, CallerContext, CancellationToken, Task<DispatchResult>> invoke)
        {
            _bindings.Add(new OperationBinding(
                descriptor,
                typeof(object),
                typeof(object),
                (arguments, caller, token) =>
                {
                    Invoked.Add(operation);
                    return invoke(arguments, caller, token);
                }));
        }

        /// <summary>
        /// The catalogue entry for the operations this job calls, so nothing here invents an
        /// exposure or a permission the product does not have.
        /// </summary>
        private static UseCaseDescriptor Descriptor(string operation) =>
            UseCaseCatalog.All.First(candidate => candidate.Name == operation);
    }
}
