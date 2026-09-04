using System.Diagnostics;
using System.Diagnostics.Metrics;
using DevBuddy.Application.Observability;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;

namespace DevBuddy.Application.Tests;

/// <summary>
/// What the pipeline reports about itself, and — the half that matters more — what it refuses to.
/// <para>
/// Telemetry usually lands somewhere less carefully guarded than the audit store, and unlike the
/// audit store it is not scoped to a workspace or gated by a permission. So the rule
/// <see cref="DevBuddyTelemetry"/> writes down is stricter than SB-19's, and these tests are what
/// keep it true: an operation name, an outcome, a channel, and a scanner rule name. Never a
/// tenant identifier, never a resource reference, never content.
/// </para>
/// </summary>
public sealed class TelemetryTests
{
    [Fact]
    public async Task an_operation_is_counted_with_its_outcome_and_channel()
    {
        using var metrics = new MetricCollector();
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft();

        await harness.SucceedAsync(
            new GetRecordUseCase(harness.Ports),
            new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id));

        Measurement counted = Assert.Single(
            metrics.Measurements, measurement => measurement.Instrument == "devbuddy.operations");

        Assert.Equal("get_record", counted.Tags["devbuddy.operation"]);
        Assert.Equal("Succeeded", counted.Tags["devbuddy.outcome"]);
        Assert.Equal("Human", counted.Tags["devbuddy.channel"]);

        Assert.Contains(
            metrics.Measurements,
            measurement => measurement.Instrument == "devbuddy.operation.duration");
    }

    [Fact]
    public async Task a_denial_is_counted_as_an_outcome_rather_than_lost()
    {
        using var metrics = new MetricCollector();
        var harness = new Harness();
        harness.Authorization.Allow = false;

        await harness.RunAsync(
            new GetRecordUseCase(harness.Ports),
            new GetRecordRequest(TestData.Scope, KnowledgeRecordId.New()));

        Measurement counted = Assert.Single(
            metrics.Measurements, measurement => measurement.Instrument == "devbuddy.operations");

        // Counted, and counted as Denied. A control doing its job is a thing an operator should be
        // able to see the rate of, not something that shows up as an error or as nothing at all.
        Assert.Equal("Denied", counted.Tags["devbuddy.outcome"]);
    }

    [Fact]
    public async Task a_blocked_secret_is_counted_by_rule_and_never_by_value()
    {
        using var metrics = new MetricCollector();
        var harness = new Harness();

        await harness.RunAsync(
            new CreateDraftUseCase(harness.Ports, harness.Ports),
            new CreateDraftRequest(
                TestData.Scope, TestData.WorkItem, RecordKind.Decision,
                "Title", "token=SECRET", TestData.Provenance));

        Measurement blocked = Assert.Single(
            metrics.Measurements, measurement => measurement.Instrument == "devbuddy.content.blocked");

        Assert.Equal("secret", blocked.Tags["devbuddy.control"]);
        Assert.Equal("literal", blocked.Tags["devbuddy.rule"]);

        // The rule that caught it, never the text it caught. A metric tag carrying the matched
        // value would put the secret into every dashboard that ever rendered this series.
        Assert.DoesNotContain(
            metrics.Measurements,
            measurement => measurement.Tags.Values.Any(
                value => value?.Contains("SECRET", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task no_tag_on_any_instrument_carries_a_tenant_identifier()
    {
        using var metrics = new MetricCollector();
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft();

        await harness.SucceedAsync(
            new GetRecordUseCase(harness.Ports),
            new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id));

        string[] identifiers =
        [
            TestData.Workspace.Value.ToString(),
            TestData.ProjectAlpha.Value.ToString(),
            TestData.Author.Value.ToString(),
            harness.Ports.Record.Id.Value.ToString(),
        ];

        foreach (Measurement measurement in metrics.Measurements)
        {
            foreach (string? value in measurement.Tags.Values)
            {
                Assert.DoesNotContain(identifiers, identifier =>
                    value?.Contains(identifier, StringComparison.OrdinalIgnoreCase) == true);
            }
        }
    }

    [Fact]
    public async Task a_span_carries_the_operation_and_outcome_and_nothing_a_caller_supplied()
    {
        using var spans = new SpanCollector();
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft("A body with SECRET material in it.");

        await harness.SucceedAsync(
            new GetRecordUseCase(harness.Ports),
            new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id));

        Activity span = Assert.Single(spans.Finished);

        Assert.Equal("get_record", span.DisplayName);
        Assert.Equal("get_record", span.GetTagItem("devbuddy.operation"));
        Assert.Equal("Succeeded", span.GetTagItem("devbuddy.outcome"));
        Assert.Equal("Human", span.GetTagItem("devbuddy.channel"));

        foreach (KeyValuePair<string, string?> tag in span.Tags)
        {
            Assert.DoesNotContain("SECRET", tag.Value ?? string.Empty, StringComparison.Ordinal);

            Assert.DoesNotContain(
                TestData.Workspace.Value.ToString(), tag.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                TestData.ProjectAlpha.Value.ToString(), tag.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task a_rejection_message_never_reaches_a_span()
    {
        using var spans = new SpanCollector();
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft();

        // A draft cannot be published, so the domain refuses and the pipeline reports Rejected.
        // The refusal names the record; the span must not.
        UseCaseResult<LifecycleResult> result = await harness.RunAsync(
            new PublishRecordUseCase(harness.Ports, harness.Ports),
            new RecordActionRequest(TestData.Scope, harness.Ports.Record.Id));

        Assert.Equal(ExecutionOutcome.Rejected, result.Outcome);

        Activity span = Assert.Single(spans.Finished);
        Assert.Equal("Rejected", span.GetTagItem("devbuddy.outcome"));

        Assert.DoesNotContain(span.Tags, tag =>
            tag.Value?.Contains("approved record", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>One measurement, flattened so a test can read its tags without generics.</summary>
    private sealed record Measurement(string Instrument, IReadOnlyDictionary<string, string?> Tags);

    /// <summary>
    /// Marks the async flow of one test, so a collector records only what that test caused.
    /// <para>
    /// Both listeners below are process-wide: a <see cref="MeterListener"/> sees every meter in
    /// the process and an <see cref="ActivityListener"/> every activity, including those of the
    /// tests xUnit is running in parallel with this one. Without this marker, `Assert.Single` is
    /// an assertion about the whole test run rather than about this test, and it fails as soon as
    /// two telemetry-producing tests overlap. Both callbacks run synchronously inside the flow
    /// that recorded the measurement or stopped the activity, so a value set here is visible
    /// there and nowhere else.
    /// </para>
    /// </summary>
    private static readonly AsyncLocal<string?> Flow = new();

    /// <summary>Claims the current flow for one collector. One collector per test.</summary>
    private static string ClaimFlow()
    {
        string token = Guid.NewGuid().ToString("N");
        Flow.Value = token;
        return token;
    }

    /// <summary>Listens to the application's own meter and records what it published.</summary>
    private sealed class MetricCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<Measurement> _measurements = [];
        private readonly string _flow = ClaimFlow();

        public MetricCollector()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == DevBuddyTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>(Record);
            _listener.SetMeasurementEventCallback<double>(Record);
            _listener.Start();
        }

        public IReadOnlyList<Measurement> Measurements
        {
            get
            {
                lock (_measurements)
                {
                    return [.. _measurements];
                }
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Record<T>(
            Instrument instrument,
            T measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            if (Flow.Value != _flow)
            {
                return;
            }

            Dictionary<string, string?> flattened = new(StringComparer.Ordinal);

            foreach (KeyValuePair<string, object?> tag in tags)
            {
                flattened[tag.Key] = tag.Value?.ToString();
            }

            lock (_measurements)
            {
                _measurements.Add(new Measurement(instrument.Name, flattened));
            }
        }
    }

    /// <summary>
    /// Listens to the application's activity source. Sampling every activity is what makes the
    /// spans exist at all — without a listener that returns AllData, StartActivity returns null
    /// and these tests would pass by measuring nothing.
    /// </summary>
    private sealed class SpanCollector : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly List<Activity> _finished = [];
        private readonly string _flow = ClaimFlow();

        public SpanCollector()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == DevBuddyTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    if (Flow.Value != _flow)
                    {
                        return;
                    }

                    lock (_finished)
                    {
                        _finished.Add(activity);
                    }
                },
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyList<Activity> Finished
        {
            get
            {
                lock (_finished)
                {
                    return [.. _finished];
                }
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
