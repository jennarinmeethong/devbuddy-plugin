namespace DevBuddy.Infrastructure.Observability;

/// <summary>
/// Where traces, metrics, and logs are shipped, and whether they are shipped at all.
/// <para>
/// Off by default. An installation that configures nothing exports nothing, opens no connection,
/// and registers no instrumentation — the same posture the outbound allow-list takes (SB-03).
/// Setting <see cref="Endpoint"/> is the whole opt-in.
/// </para>
/// </summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>
    /// An OTLP endpoint — a collector, usually. Empty means telemetry stays in the process and
    /// goes nowhere, which is the default.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// What this process calls itself in a trace. Each host sets its own; an operator only
    /// overrides it when running two of the same host against one collector.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// The fraction of traces recorded, 0 to 1. Metrics and logs are unaffected — they are
    /// aggregates and a sample of them is a wrong number rather than a smaller one.
    /// </summary>
    public double TraceSampleRatio { get; set; } = 1.0;

    /// <summary>
    /// Whether application logs are shipped over OTLP alongside traces and metrics.
    /// <para>
    /// Defaults to off, and the reason is not performance: shipping logs to a collector is a third
    /// path out of the boundary, beside the AI channel and the trace exporter, and one an
    /// installation should cross on purpose.
    /// </para>
    /// <para>
    /// It used to be off for a sharper reason — with no SMTP configured, setup and recovery tokens
    /// were written to the log unconditionally, so turning this on copied single-use credentials
    /// into whatever collected them. Phase 12B closed that at the source:
    /// <c>Email:AllowTokensInLog</c> is false, so nothing writes a token to a log unless an
    /// operator asked for it. An operator who does ask should leave this off, or accept that the
    /// tokens travel with the logs. <c>docker/compose.observability.yaml</c> turns this on, which
    /// is option 2 in <c>docs/operations/logging.md</c>.
    /// </para>
    /// </summary>
    public bool ExportLogs { get; set; }

    /// <summary>True when an endpoint is configured and the endpoint is a usable absolute URI.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) && Uri.TryCreate(Endpoint, UriKind.Absolute, out _);
}
