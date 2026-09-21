using DevBuddy.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// With the file sink on, Serilog owns the logging pipeline, and a provider registered after it, such
/// as the OpenTelemetry log exporter, used to receive nothing. docker/compose.yaml always turns the
/// file sink on, so logs never reached Loki through the observability overlay. The observability
/// end-to-end test found it in Phase 13.
/// </summary>
public sealed class FileLoggingForwardingTests
{
    [Fact]
    public void a_provider_registered_after_the_file_sink_still_receives_log_lines()
    {
        string directory = Path.Combine(Path.GetTempPath(), "devbuddy-logs-" + Guid.NewGuid().ToString("N"));

        try
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [$"{LogFileOptions.SectionName}:Path"] = Path.Combine(directory, "devbuddy.log"),
                })
                .Build();

            var recorder = new RecordingProvider();
            ServiceCollection services = new();
            services.AddDevBuddyFileLogging(configuration);

            // Where AddDevBuddyTelemetry registers the OpenTelemetry exporter: after the file sink.
            services.AddLogging(logging => logging.AddProvider(recorder));

            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                provider.GetRequiredService<ILogger<FileLoggingForwardingTests>>().Log(
                    LogLevel.Information, new EventId(1), "forwarded to every provider", null, (state, _) => state);
            }

            Assert.Contains(recorder.Messages, message => message.Contains("forwarded to every provider", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class RecordingProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Recording(Messages);

        public void Dispose()
        {
        }

        private sealed class Recording(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (messages)
                {
                    messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
