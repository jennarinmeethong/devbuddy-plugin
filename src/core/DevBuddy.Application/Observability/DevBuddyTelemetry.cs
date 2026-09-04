using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DevBuddy.Application.Observability;

/// <summary>
/// The instruments the pipeline writes to, and the rule about what may be written to them.
/// <para>
/// Built on <see cref="ActivitySource"/> and <see cref="Meter"/> from the base library rather than
/// on OpenTelemetry types, so the Application layer keeps its "Domain only, no packages" shape.
/// Which exporter collects these, and whether anything collects them at all, is decided in
/// Infrastructure and by configuration — nothing here reaches a network.
/// </para>
/// <para>
/// <b>What may be a tag, and what may not.</b> Telemetry usually lands somewhere less carefully
/// guarded than the audit store, and unlike the audit store it is not scoped to a workspace or
/// gated by a permission. So the rule here is stricter than SB-19's: an operation name, an
/// outcome, a channel, and a scanner rule name — a fixed vocabulary, all of it. Never a workspace
/// or project identifier, never a resource reference, never a denial reason, and never anything a
/// caller supplied. A metric tag carrying a tenant identifier would let anybody with a dashboard
/// enumerate the installation; a span carrying a rejection message would put record titles and
/// identifiers into a system nobody thinks of as holding them.
/// </para>
/// </summary>
public static class DevBuddyTelemetry
{
    /// <summary>The name a tracing exporter subscribes to.</summary>
    public const string ActivitySourceName = "DevBuddy.Operations";

    /// <summary>The name a metrics exporter subscribes to.</summary>
    public const string MeterName = "DevBuddy";

    internal static readonly ActivitySource Operations = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> OperationCount = Meter.CreateCounter<long>(
        "devbuddy.operations",
        unit: "{operation}",
        description: "Operations run through the pipeline, by outcome and channel.");

    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "devbuddy.operation.duration",
        unit: "ms",
        description: "How long an operation took, from validation to audit.");

    private static readonly Counter<long> ContentBlocked = Meter.CreateCounter<long>(
        "devbuddy.content.blocked",
        unit: "{finding}",
        description: "Content refused before it was stored, by the control and rule that caught it.");

    private static readonly Histogram<double> AnalysisDuration = Meter.CreateHistogram<double>(
        "devbuddy.analysis.duration",
        unit: "ms",
        description: "How long one read-only analysis took, against the ceiling that bounds it (SB-22).");

    private static readonly Counter<long> AnalysisRejected = Meter.CreateCounter<long>(
        "devbuddy.analysis.rejected",
        unit: "{analysis}",
        description: "Analyses refused because the concurrency ceiling was already reached (SB-21).");

    /// <summary>
    /// One finished operation. The outcome is the pipeline's own enum name, so a dashboard can
    /// tell a denial from a refusal from a crash without anybody parsing a message.
    /// </summary>
    public static void RecordOperation(string operation, string outcome, string channel, TimeSpan elapsed)
    {
        TagList tags = new()
        {
            { "devbuddy.operation", operation },
            { "devbuddy.outcome", outcome },
            { "devbuddy.channel", channel },
        };

        OperationCount.Add(1, tags);
        OperationDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    /// <summary>
    /// One scanner finding that stopped a write (SB-17, SB-18). The rule name is a fixed
    /// vocabulary from the two rule sets — <c>aws-access-key-id</c>, <c>us-ssn</c> — and never the
    /// text that matched it.
    /// </summary>
    public static void RecordBlockedContent(string control, string rule) =>
        ContentBlocked.Add(1, new TagList { { "devbuddy.control", control }, { "devbuddy.rule", rule } });

    /// <summary>
    /// One completed analysis. The kind is the <c>AnalysisKind</c> enum name; the project it ran
    /// against is deliberately not a tag.
    /// </summary>
    public static void RecordAnalysis(string kind, TimeSpan elapsed) =>
        AnalysisDuration.Record(elapsed.TotalMilliseconds, new TagList { { "devbuddy.analysis_kind", kind } });

    /// <summary>
    /// One analysis refused at the door. This climbing is the signal that the ceiling is too low
    /// for the load, or that something is looping over projects — the two cases SB-21 exists for.
    /// </summary>
    public static void RecordAnalysisRejected(string kind) =>
        AnalysisRejected.Add(1, new TagList { { "devbuddy.analysis_kind", kind } });
}
