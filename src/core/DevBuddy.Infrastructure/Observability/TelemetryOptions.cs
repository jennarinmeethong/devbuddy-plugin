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
    /// Defaults to off, and the reason is not performance. With no SMTP configured, account setup
    /// and recovery tokens are written to the log on purpose (see <c>LogEmailSender</c>) — turning
    /// this on without turning SMTP on first copies those single-use credentials into whatever
    /// collects them. Configure <c>Email:Provider = Smtp</c> first, or accept that consequence
    /// knowingly.
    /// </para>
    /// </summary>
    public bool ExportLogs { get; set; }

    /// <summary>True when an endpoint is configured and the endpoint is a usable absolute URI.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) && Uri.TryCreate(Endpoint, UriKind.Absolute, out _);
}
