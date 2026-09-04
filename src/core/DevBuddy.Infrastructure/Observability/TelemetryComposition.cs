using DevBuddy.Application.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DevBuddy.Infrastructure.Observability;

/// <summary>
/// Wires OpenTelemetry, when an operator has asked for it.
/// <para>
/// `docs/plan.md` named this in its technology choices from the start and it is only now being
/// picked up, so it arrives with the same posture everything else outward-facing here has: off
/// unless configured, and narrow when it is on.
/// </para>
/// </summary>
public static class TelemetryComposition
{
    /// <summary>
    /// Registers tracing, metrics, and — only if asked — log export.
    /// <para>
    /// Returns without registering anything when no endpoint is configured, rather than
    /// registering a pipeline that exports into the void. An installation that wants none of this
    /// pays nothing for it.
    /// </para>
    /// </summary>
    /// <param name="configureTracing">
    /// Instrumentation only a particular host can add. The ASP.NET Core instrumentation lives
    /// here rather than in this project because its package carries a framework reference to
    /// Microsoft.AspNetCore.App: taking it as an Infrastructure dependency propagated that to the
    /// console, whose image is built on the smaller runtime base, and stopped it starting at all.
    /// </param>
    /// <param name="configureMetrics">The same, for meters a host owns.</param>
    public static IServiceCollection AddDevBuddyTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        TelemetryOptions options = new();
        configuration.GetSection(TelemetryOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            options.ServiceName = serviceName;
        }

        services.AddSingleton(options);

        if (!options.IsConfigured)
        {
            return services;
        }

        var endpoint = new Uri(options.Endpoint);

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new TraceIdRatioBasedSampler(options.TraceSampleRatio))
                    .AddSource(DevBuddyTelemetry.ActivitySourceName)
                    .AddHttpClientInstrumentation();

                configureTracing?.Invoke(tracing);

                tracing
                    // Runs last, so it sees every tag any instrumentation added — including
                    // whatever the host just contributed.
                    .AddProcessor(new ScrubIdentifiersProcessor())
                    .AddOtlpExporter(exporter => exporter.Endpoint = endpoint);

                // Database instrumentation is deliberately absent. Npgsql and the EF Core
                // instrumentation both put statement text on a span, and while this codebase
                // parameterises everything — so values are not in that text — a query shape is
                // still more than the fixed vocabulary the tagging rule allows. Database time
                // shows up inside the operation span's own duration, which is the question an
                // operator is actually asking.
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(DevBuddyTelemetry.MeterName)
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                configureMetrics?.Invoke(metrics);

                metrics.AddOtlpExporter(exporter => exporter.Endpoint = endpoint);
            });

        if (options.ExportLogs)
        {
            services.AddLogging(logging => logging.AddOpenTelemetry(telemetry =>
            {
                telemetry.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(options.ServiceName));
                telemetry.AddOtlpExporter(exporter => exporter.Endpoint = endpoint);
            }));
        }

        return services;
    }
}
