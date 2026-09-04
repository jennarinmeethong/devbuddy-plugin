namespace DevBuddy.Infrastructure.Observability;

/// <summary>
/// Where the application writes its own log files, and for how long it keeps them.
/// <para>
/// This is the one row of the retention schedule that used to sit outside this codebase entirely.
/// The container's log driver bounds output by size, which is a real bound and not the 90 days the
/// schedule names; a file the application writes and sweeps itself is a window a test can assert
/// against. `docs/operations/logging.md` sets out the alternatives and why this one costs
/// something.
/// </para>
/// <para>
/// <b>Off unless <see cref="Path"/> is set.</b> The shipped containers run with a read-only root
/// filesystem, so a default that wrote files would make every plain `docker run` of these images
/// fail at startup. `docker/compose.yaml` sets the path and mounts a volume for it; anything else
/// keeps logging to standard output only, exactly as before.
/// </para>
/// </summary>
public sealed class LogFileOptions
{
    public const string SectionName = "Logging:File";

    /// <summary>
    /// The log file to write. Rolled daily, with the date inserted before the extension, so
    /// <c>/var/log/devbuddy/devbuddy.log</c> produces <c>devbuddy20260904.log</c> and its
    /// siblings. Empty means no file logging at all.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// How long a rolled file is kept. Ninety days, matching the retention schedule.
    /// <para>
    /// Enforced twice, on purpose. Serilog drops files past the limit when it rolls, which handles
    /// a running service; `dotnet run -- retention` sweeps them too, which handles one that has
    /// been stopped for a month and is the half a test can drive deterministically.
    /// </para>
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// A ceiling per file, so one pathological loop cannot fill the volume before the daily roll
    /// (SB-21). Writes past it are dropped rather than rolling into a second file for the same
    /// day, which is the behaviour that keeps the day-to-file mapping the sweep relies on.
    /// </summary>
    public long FileSizeLimitBytes { get; set; } = 128L * 1024 * 1024;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Path);
}
