using System.CommandLine;
using System.Globalization;
using DevBuddy.Application.Abstractions;
using DevBuddy.Cli;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The scheduling mode on <c>retention</c>, which is Phase 12B's exit criterion: the shipped stack
/// schedules the sweep, and the schedule invokes the same sweep the console command does.
/// <para>
/// "The same sweep" is the claim worth testing, because the easy way to build this would have been
/// a second code path that agrees with the first today. These drive both entry points against one
/// counter and one report writer and check they are indistinguishable.
/// </para>
/// <para>
/// No clock is waited on. The wait between passes is a delegate so a test can drive a year of
/// schedule in a millisecond, and the interval parser is checked separately from the loop.
/// </para>
/// </summary>
public sealed class RetentionScheduleTests
{
    private static readonly RetentionReport Nothing = new(0, 0, 0, 0, 0, 0);

    [Fact]
    public async Task a_single_pass_sweeps_once()
    {
        int passes = 0;
        StringWriter output = new();

        await RetentionSchedule.SweepOnceAsync(
            _ => { passes++; return Task.FromResult(Nothing); }, output, CancellationToken.None);

        Assert.Equal(1, passes);
    }

    [Fact]
    public async Task the_schedule_sweeps_once_per_interval()
    {
        int passes = 0;
        using CancellationTokenSource stopping = new();

        await RetentionSchedule.SweepEveryAsync(
            _ => { passes++; return Task.FromResult(Nothing); },
            TimeSpan.FromHours(24),
            (_, _) =>
            {
                // Three waits, then stop. The loop checks cancellation at the top, so this leaves
                // exactly three passes behind.
                if (passes >= 3)
                {
                    stopping.Cancel();
                }

                return Task.CompletedTask;
            },
            new StringWriter(),
            new StringWriter(),
            stopping.Token);

        Assert.Equal(3, passes);
    }

    /// <summary>
    /// The exit criterion, stated as an assertion. Both entry points call the identical delegate
    /// and produce the identical report, so there is one sweep with two ways to start it rather
    /// than two implementations that happen to agree.
    /// </summary>
    [Fact]
    public async Task the_scheduled_pass_and_the_single_pass_are_the_same_sweep()
    {
        RetentionReport report = new(4, 3, 2, 1, 5, 6);
        List<string> invoked = [];

        Task<RetentionReport> Sweep(CancellationToken _)
        {
            invoked.Add("IRetentionEnforcer.ApplyAsync");
            return Task.FromResult(report);
        }

        StringWriter single = new();
        await RetentionSchedule.SweepOnceAsync(Sweep, single, CancellationToken.None);

        StringWriter scheduled = new();
        using CancellationTokenSource stopping = new();

        await RetentionSchedule.SweepEveryAsync(
            Sweep,
            TimeSpan.FromHours(6),
            (_, _) => { stopping.Cancel(); return Task.CompletedTask; },
            scheduled,
            new StringWriter(),
            stopping.Token);

        Assert.Equal(2, invoked.Count);
        Assert.All(invoked, entry => Assert.Equal("IRetentionEnforcer.ApplyAsync", entry));

        // The scheduled output is the single-pass output with the schedule's own two lines around
        // it. Anything else would mean a second reporting path to keep in step.
        Assert.Contains(single.ToString().Trim(), scheduled.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task every_count_the_sweep_reports_is_printed()
    {
        StringWriter output = new();

        await RetentionSchedule.SweepOnceAsync(
            _ => Task.FromResult(new RetentionReport(11, 12, 13, 14, 15, 0)),
            output,
            CancellationToken.None);

        foreach (int count in (int[])[11, 12, 13, 14, 15])
        {
            Assert.Contains(
                count.ToString(CultureInfo.InvariantCulture),
                output.ToString(),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task eligible_archived_records_are_reported_and_not_deleted()
    {
        StringWriter output = new();

        await RetentionSchedule.SweepOnceAsync(
            _ => Task.FromResult(new RetentionReport(0, 0, 0, 0, 0, 7)),
            output,
            CancellationToken.None);

        Assert.Contains("reported, not deleted", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A schedule meant to run for months has to survive the database being briefly unreachable.
    /// One that exited on the first restart would leave the window unswept until somebody noticed
    /// the container had stopped, which is worse than the failure it exited on.
    /// </summary>
    [Fact]
    public async Task a_failed_pass_is_reported_and_the_schedule_continues()
    {
        int passes = 0;
        StringWriter errors = new();
        using CancellationTokenSource stopping = new();

        await RetentionSchedule.SweepEveryAsync(
            _ =>
            {
                passes++;

                return passes == 1
                    ? throw new InvalidOperationException("the database went away")
                    : Task.FromResult(Nothing);
            },
            TimeSpan.FromMinutes(30),
            (_, _) =>
            {
                if (passes >= 2)
                {
                    stopping.Cancel();
                }

                return Task.CompletedTask;
            },
            new StringWriter(),
            errors,
            stopping.Token);

        Assert.Equal(2, passes);
        Assert.Contains("the database went away", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("the schedule continues", errors.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Immediately, then on the interval. A container started with <c>--every 24h</c> that waited
    /// a day first would leave an operator unable to tell a working schedule from a broken one.
    /// </summary>
    [Fact]
    public async Task the_first_pass_does_not_wait_for_the_interval()
    {
        int passes = 0;
        List<TimeSpan> waited = [];
        using CancellationTokenSource stopping = new();

        await RetentionSchedule.SweepEveryAsync(
            _ => { passes++; return Task.FromResult(Nothing); },
            TimeSpan.FromDays(1),
            (interval, _) => { waited.Add(interval); stopping.Cancel(); return Task.CompletedTask; },
            new StringWriter(),
            new StringWriter(),
            stopping.Token);

        Assert.Equal(1, passes);
        Assert.Equal([TimeSpan.FromDays(1)], waited);
    }

    [Fact]
    public async Task a_cancelled_schedule_stops_without_another_pass()
    {
        int passes = 0;
        using CancellationTokenSource stopping = new();
        await stopping.CancelAsync();

        await RetentionSchedule.SweepEveryAsync(
            _ => { passes++; return Task.FromResult(Nothing); },
            TimeSpan.FromHours(1),
            (_, _) => Task.CompletedTask,
            new StringWriter(),
            new StringWriter(),
            stopping.Token);

        Assert.Equal(0, passes);
    }

    [Theory]
    [InlineData("60s", 60)]
    [InlineData("90m", 5400)]
    [InlineData("24h", 86400)]
    [InlineData("7d", 604800)]
    [InlineData("1.5h", 5400)]
    [InlineData("06:00:00", 21600)]
    [InlineData("  24h  ", 86400)]
    public void an_interval_is_read_the_way_an_operator_writes_one(string text, int seconds)
    {
        Assert.True(RetentionSchedule.TryParseInterval(text, out TimeSpan interval, out string? problem));
        Assert.Null(problem);
        Assert.Equal(TimeSpan.FromSeconds(seconds), interval);
    }

    /// <summary>
    /// The reason this is not a plain <see cref="TimeSpan"/> option: <c>TimeSpan.Parse("24")</c>
    /// succeeds and means twenty-four <em>days</em>. A schedule that read <c>--every 24</c> as
    /// monthly would look configured and be wrong, silently, for three and a half weeks.
    /// </summary>
    [Fact]
    public void a_bare_number_is_refused_rather_than_read_as_days()
    {
        Assert.False(RetentionSchedule.TryParseInterval("24", out _, out string? problem));
        Assert.NotNull(problem);
        Assert.Contains("24h", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("soon")]
    [InlineData("h")]
    [InlineData("-4h")]
    [InlineData("0h")]
    public void an_interval_that_is_not_one_is_refused_with_a_reason(string text)
    {
        Assert.False(RetentionSchedule.TryParseInterval(text, out _, out string? problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Theory]
    [InlineData("1s")]
    [InlineData("30s")]
    [InlineData("00:00:10")]
    public void an_interval_shorter_than_a_minute_is_refused(string text)
    {
        // Not a performance limit. A pass reads every audit, backup, export and log row, and
        // --every 1s is a busy loop against the database the servers are using.
        Assert.False(RetentionSchedule.TryParseInterval(text, out _, out string? problem));
        Assert.Contains("shortest interval", problem!, StringComparison.Ordinal);
    }

    [Fact]
    public void the_console_accepts_the_interval_on_the_retention_command()
    {
        ParseResult parsed = CommandSurface.Build().Parse(
            ["retention", "--every", "24h"], CommandSurface.Parsing);

        Assert.Empty(parsed.Errors);
    }

    [Fact]
    public void the_console_still_accepts_a_single_pass()
    {
        ParseResult parsed = CommandSurface.Build().Parse(["retention"], CommandSurface.Parsing);

        Assert.Empty(parsed.Errors);
    }
}
