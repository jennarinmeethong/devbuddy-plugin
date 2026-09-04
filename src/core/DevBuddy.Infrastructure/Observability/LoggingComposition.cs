using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace DevBuddy.Infrastructure.Observability;

/// <summary>
/// Gives the application a log file it owns, when an operator has asked for one.
/// <para>
/// Option 3 of `docs/operations/logging.md`, and the only one of the three that puts a 90-day
/// window inside something a test can drive. It costs what that file says it costs: a dependency,
/// and file handling inside an image that otherwise has no opinion about disk.
/// </para>
/// </summary>
public static class LoggingComposition
{
    /// <summary>
    /// Registers Serilog as the logging pipeline, writing to the console and to a daily file.
    /// <para>
    /// Returns without touching anything when no path is configured, which is the default: the
    /// hosts then keep the framework's console provider and the container's log driver keeps
    /// owning retention, exactly as before.
    /// </para>
    /// <para>
    /// A configured path that cannot be written fails at startup rather than degrading to console
    /// only. An operator who asked for files and silently got none would discover it when they
    /// went looking for a log that was never written.
    /// </para>
    /// </summary>
    /// <param name="logToStandardError">
    /// True for the MCP server under stdio, where stdout is the protocol and anything else written
    /// there corrupts the stream.
    /// </param>
    public static IServiceCollection AddDevBuddyFileLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool logToStandardError = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bound again rather than resolved: this runs while the collection is still being built,
        // and AddDevBuddy has already registered the instance the retention sweep will use.
        LogFileOptions options = new();
        configuration.GetSection(LogFileOptions.SectionName).Bind(options);

        if (!options.IsConfigured)
        {
            return services;
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(options.Path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                formatProvider: CultureInfo.InvariantCulture,
                standardErrorFromLevel: logToStandardError ? LogEventLevel.Verbose : null)
            .WriteTo.File(
                path: options.Path,
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,

                // Both halves of the window. Serilog drops files past the limit as it rolls, which
                // covers a service that keeps running; `dotnet run -- retention` sweeps the same
                // directory, which covers one that has been stopped and is what a test can drive.
                retainedFileTimeLimit: TimeSpan.FromDays(options.RetentionDays),
                fileSizeLimitBytes: options.FileSizeLimitBytes,

                // Deliberately not rolling on the size limit: one file per day is what makes the
                // name parseable, and therefore what makes the sweep able to decide a file's age
                // without trusting a filesystem timestamp that a copy would have reset.
                rollOnFileSizeLimit: false)
            .CreateLogger();

        // Serilog owns the pipeline once it is on, rather than sitting alongside the framework's
        // console provider and printing everything twice.
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSerilog(logger, dispose: true);

        return services;
    }
}
