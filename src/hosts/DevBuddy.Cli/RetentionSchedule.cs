using System.Globalization;
using System.Runtime.InteropServices;
using DevBuddy.Application.Abstractions;

namespace DevBuddy.Cli;

/// <summary>
/// One retention pass, and the loop that repeats it.
/// <para>
/// The shipped stack had no scheduler at all: <c>docker/compose.yaml</c> carried
/// <c>migrate</c>, <c>api</c>, <c>mcp</c>, <c>database</c> and <c>evidence</c>, so the sweep ran
/// only if an operator built something to run it. A window nothing sweeps is a window in name
/// only, which is what Phase 12B set out to take back from the operator.
/// </para>
/// <para>
/// A scheduling mode on the console rather than a <c>cron</c> sidecar, and the images are the
/// reason. They are chiseled — no shell, no package manager, nothing to execute — so a sidecar
/// running <c>cron</c> would mean building a shell-bearing image for the purpose and putting it
/// back into a stack that was made this way on purpose. This keeps the schedule inside the one
/// image that already carries the code.
/// </para>
/// <para>
/// It stays <b>outside</b> the use-case pipeline for the reason the sweep was put there: a pass
/// spans every workspace and project and has no caller to authorise it against, exactly like
/// <c>restore</c>. A scheduler that acquired a caller would be the installation-wide superuser
/// this system does not have. Nothing here resolves an actor, a membership, or a tenant context.
/// </para>
/// </summary>
internal static class RetentionSchedule
{
    /// <summary>
    /// The shortest interval accepted. Not a performance limit — a pass reads every audit,
    /// backup, export and log row — but a guard against <c>--every 0</c> or <c>--every 1s</c>
    /// turning a schedule into a busy loop against the database the servers are using.
    /// </summary>
    public static readonly TimeSpan ShortestInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Runs one pass and reports it. Both <c>retention</c> and <c>retention --every</c> come
    /// through here, which is what makes them the same sweep rather than two that agree today.
    /// </summary>
    public static async Task SweepOnceAsync(
        Func<CancellationToken, Task<RetentionReport>> sweep,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        RetentionReport report = await sweep(cancellationToken);

        output.WriteLine($"audit events deleted        {report.AuditEventsDeleted}");
        output.WriteLine($"orphaned evidence deleted    {report.OrphanedEvidenceDeleted}");
        output.WriteLine($"backups deleted              {report.BackupsDeleted}");
        output.WriteLine($"exports deleted              {report.ExportsDeleted}");
        output.WriteLine($"log files deleted            {report.LogFilesDeleted}");
        output.WriteLine($"archived records eligible    {report.ArchivedRecordsEligibleForDeletion}");

        if (report.ArchivedRecordsEligibleForDeletion > 0)
        {
            output.WriteLine(
                "Eligible archived records are reported, not deleted: that needs the owner's "
                + "request.");
        }
    }

    /// <summary>
    /// Sweeps immediately and then every <paramref name="interval"/> until cancelled.
    /// <para>
    /// Immediately first, deliberately. A container started with <c>--every 24h</c> that waited a
    /// day before its first pass would leave an operator unable to tell a working schedule from a
    /// broken one until the next morning.
    /// </para>
    /// <para>
    /// A failed pass is reported and the loop continues. The database being briefly unreachable —
    /// a restart, a failover — is the ordinary case for a process meant to run for months, and a
    /// scheduler that exited on the first of those would leave the window unswept until somebody
    /// noticed the container had stopped.
    /// </para>
    /// </summary>
    /// <param name="wait">
    /// How the wait between passes is performed. A seam, so a test can drive months of schedule
    /// without spending them.
    /// </param>
    public static async Task SweepEveryAsync(
        Func<CancellationToken, Task<RetentionReport>> sweep,
        TimeSpan interval,
        Func<TimeSpan, CancellationToken, Task> wait,
        TextWriter output,
        TextWriter errors,
        CancellationToken cancellationToken)
    {
        output.WriteLine(
            $"Applying the retention schedule now and every {Describe(interval)}. "
            + "Stop the process to stop the schedule.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(sweep, output, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                // Not swallowed — reported, with the type, so a permanent misconfiguration is
                // visible in the container log rather than only in a count that never moves.
                errors.WriteLine(
                    $"A retention pass failed and the schedule continues: "
                    + $"{failure.GetType().Name}: {failure.Message}");
            }

            try
            {
                await wait(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        output.WriteLine("The retention schedule stopped.");
    }

    /// <summary>
    /// Reads an interval the way an operator writes one: <c>24h</c>, <c>90m</c>, <c>7d</c>,
    /// <c>30s</c>, or a <c>hh:mm:ss</c> span.
    /// <para>
    /// A plain <see cref="TimeSpan"/> option would have accepted <c>24</c> as twenty-four
    /// <em>days</em>, which is the kind of quiet misreading that leaves a schedule looking
    /// configured and running once a month.
    /// </para>
    /// </summary>
    public static bool TryParseInterval(string text, out TimeSpan interval, out string? problem)
    {
        interval = default;
        problem = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            problem = "An interval is required, for example 24h.";
            return false;
        }

        string trimmed = text.Trim();
        char unit = char.ToLowerInvariant(trimmed[^1]);
        string number = trimmed[..^1];

        if ("smhd".Contains(unit, StringComparison.Ordinal)
            && double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double count))
        {
            if (count <= 0 || double.IsNaN(count) || double.IsInfinity(count))
            {
                problem = $"'{text}' is not a length of time the schedule can wait.";
                return false;
            }

            interval = unit switch
            {
                's' => TimeSpan.FromSeconds(count),
                'm' => TimeSpan.FromMinutes(count),
                'h' => TimeSpan.FromHours(count),
                _ => TimeSpan.FromDays(count),
            };
        }
        else if (!trimmed.Contains(':', StringComparison.Ordinal)
            || !TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out interval))
        {
            // The colon is required for the span form, which is what refuses a bare number.
            // TimeSpan.Parse("24") succeeds and means twenty-four *days*, so `--every 24` would
            // have looked configured and swept monthly.
            problem =
                $"'{text}' is not an interval. Write a number and a unit — 90m, 24h, 7d — "
                + "or a hh:mm:ss span.";

            return false;
        }

        if (interval < ShortestInterval)
        {
            problem =
                $"'{text}' is shorter than the shortest interval the schedule accepts "
                + $"({Describe(ShortestInterval)}). A sweep reads every audit, backup, export and "
                + "log row, and a schedule that short is a busy loop against the database the "
                + "servers are using.";

            return false;
        }

        return true;
    }

    /// <summary>
    /// Cancels when the process is asked to stop, so <c>docker compose down</c> ends a schedule
    /// cleanly rather than killing it ten seconds later.
    /// <para>
    /// SIGTERM as well as Ctrl+C: a container is stopped with the former, and System.CommandLine
    /// wires only the latter into the token it hands a command.
    /// </para>
    /// </summary>
    public static IDisposable StopOnSignal(CancellationTokenSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<IDisposable> registrations = [];

        foreach (PosixSignal signal in (PosixSignal[])[PosixSignal.SIGTERM, PosixSignal.SIGINT])
        {
            try
            {
                registrations.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    // Handled here, so the runtime does not tear the process down mid-pass.
                    context.Cancel = true;
                    source.Cancel();
                }));
            }
            catch (PlatformNotSupportedException)
            {
                // A platform without this signal still stops on cancellation of the token
                // System.CommandLine supplies; it just stops less politely.
            }
        }

        return new Registrations(registrations);
    }

    private static string Describe(TimeSpan interval) =>
        interval.TotalDays >= 1 && interval.TotalDays == Math.Floor(interval.TotalDays)
            ? Plural(interval.TotalDays, "day")
            : interval.TotalHours >= 1 && interval.TotalHours == Math.Floor(interval.TotalHours)
                ? Plural(interval.TotalHours, "hour")
                : interval.TotalMinutes >= 1 && interval.TotalMinutes == Math.Floor(interval.TotalMinutes)
                    ? Plural(interval.TotalMinutes, "minute")
                    : Plural(interval.TotalSeconds, "second");

    private static string Plural(double count, string unit) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{count:0.##} {unit}{(Math.Abs(count - 1) < 0.0001 ? string.Empty : "s")}");

    private sealed class Registrations(List<IDisposable> held) : IDisposable
    {
        public void Dispose()
        {
            foreach (IDisposable registration in held)
            {
                registration.Dispose();
            }
        }
    }
}
