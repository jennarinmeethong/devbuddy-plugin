using System.CommandLine;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Security;
using DevBuddy.Application.Workers;
using DevBuddy.Cli;
using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The worker schedule: the last piece of Phase 12C, and the thing that makes the two jobs run at
/// all.
/// <para>
/// The claims worth testing are the ones a schedule could get quietly wrong while every job test
/// stayed green. That the token is resolved on every pass, so revoking it stops the next one. That
/// the workspace entered is the token's and nothing else's. That a budget is per pass rather than
/// per process. That the channel is still the job's declaration, not something the schedule picks.
/// And that a configuration which would run without doing what it looks like is refused before
/// anything is built.
/// </para>
/// <para>
/// The job here is a fake that records what it was handed; the jobs themselves are covered by
/// their own suites. What is real is <see cref="WorkerCaller.FromMachineToken"/>, which the pass
/// goes through exactly as it does in the console.
/// </para>
/// </summary>
public sealed class WorkerScheduleTests
{
    private const string Token = "a-worker-token";

    [Fact]
    public void a_job_that_does_not_exist_is_refused()
    {
        Assert.False(WorkerSchedule.TryValidate(
            "reindex-everything", budget: null, staleAfter: null, Token, out string? problem));

        Assert.Contains("not a worker job", problem!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void a_worker_with_no_token_is_refused_rather_than_run_as_nobody(string? token)
    {
        Assert.False(WorkerSchedule.TryValidate(
            WorkerSchedule.StaleRecordSweep, budget: null, TimeSpan.FromDays(180), token, out string? problem));

        Assert.Contains(WorkerSchedule.TokenVariable, problem!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A default that spent would be a cost decision the console took for somebody, and a default of
    /// zero would be a schedule that embedded nothing every night while looking configured.
    /// </summary>
    [Fact]
    public void the_embedding_sweep_must_be_told_its_budget()
    {
        Assert.False(WorkerSchedule.TryValidate(
            WorkerSchedule.RecordEmbeddingSweep, budget: null, staleAfter: null, Token, out string? problem));

        Assert.Contains("--budget", problem!, StringComparison.Ordinal);
    }

    [Fact]
    public void a_budget_of_zero_written_out_is_accepted_as_the_off_switch()
    {
        Assert.True(WorkerSchedule.TryValidate(
            WorkerSchedule.RecordEmbeddingSweep, budget: 0, staleAfter: null, Token, out string? problem));

        Assert.Null(problem);
    }

    [Fact]
    public void a_negative_budget_is_refused_rather_than_read_as_zero()
    {
        Assert.False(WorkerSchedule.TryValidate(
            WorkerSchedule.RecordEmbeddingSweep, budget: -1, staleAfter: null, Token, out _));
    }

    [Fact]
    public void the_stale_record_sweep_must_be_told_what_counts_as_stale()
    {
        Assert.False(WorkerSchedule.TryValidate(
            WorkerSchedule.StaleRecordSweep, budget: null, staleAfter: null, Token, out string? problem));

        Assert.Contains("--stale-after", problem!, StringComparison.Ordinal);

        Assert.True(WorkerSchedule.TryValidate(
            WorkerSchedule.StaleRecordSweep, budget: null, TimeSpan.FromDays(180), Token, out _));
    }

    /// <summary>
    /// A flag the chosen job would ignore reads as a flag that works. Refused both ways round.
    /// </summary>
    [Fact]
    public void a_setting_the_job_would_ignore_is_refused()
    {
        Assert.False(WorkerSchedule.TryValidate(
            WorkerSchedule.RecordEmbeddingSweep, budget: 10, TimeSpan.FromDays(1), Token, out _));

        Assert.False(WorkerSchedule.TryValidate(
            WorkerSchedule.StaleRecordSweep, budget: 10, TimeSpan.FromDays(180), Token, out _));
    }

    [Fact]
    public async Task a_token_that_resolves_to_nobody_refuses_the_pass_and_runs_nothing()
    {
        RecordingJob job = new(WorkerModelUse.None);
        bool entered = false;

        WorkerRunReport report = await WorkerSchedule.PassAsync(
            job,
            _ => Task.FromResult<MachineTokenIdentity?>(null),
            _ => entered = true,
            budget: 0,
            CancellationToken.None);

        Assert.True(report.Refused);
        Assert.Contains("resolved to nobody", report.Reason!, StringComparison.Ordinal);
        Assert.Empty(job.Callers);
        Assert.False(entered);
    }

    /// <summary>
    /// The reason the token is resolved per pass. Revoking a worker's token on the Teams screen is
    /// how an administrator stops it, and that has to take effect at the next pass rather than at
    /// the next deployment.
    /// </summary>
    [Fact]
    public async Task a_token_revoked_between_two_passes_stops_the_second()
    {
        RecordingJob job = new(WorkerModelUse.None);
        MachineTokenIdentity identity = Identity();
        bool revoked = false;
        int resolutions = 0;
        List<WorkerRunReport> reports = [];
        using CancellationTokenSource stopping = new();

        await WorkerSchedule.RunEveryAsync(
            job.Name,
            async token =>
            {
                WorkerRunReport report = await WorkerSchedule.PassAsync(
                    job,
                    _ =>
                    {
                        resolutions++;
                        return Task.FromResult(revoked ? null : identity);
                    },
                    _ => { },
                    budget: 0,
                    token);

                reports.Add(report);
                return report;
            },
            TimeSpan.FromHours(24),
            (_, _) =>
            {
                // Revoked after the first pass; stopped after the second.
                if (reports.Count == 1)
                {
                    revoked = true;
                }
                else
                {
                    stopping.Cancel();
                }

                return Task.CompletedTask;
            },
            new StringWriter(),
            new StringWriter(),
            stopping.Token);

        Assert.Equal(2, resolutions);
        Assert.Single(job.Callers);
        Assert.False(reports[0].Refused);
        Assert.True(reports[1].Refused);
    }

    /// <summary>
    /// The workspace is taken from the resolved credential. Nothing a job or a schedule was
    /// configured with can name another one.
    /// </summary>
    [Fact]
    public async Task the_workspace_entered_is_the_one_the_token_was_minted_in()
    {
        MachineTokenIdentity identity = Identity();
        WorkspaceId? entered = null;
        RecordingJob job = new(WorkerModelUse.None);

        await WorkerSchedule.PassAsync(
            job,
            _ => Task.FromResult<MachineTokenIdentity?>(identity),
            workspace => entered = workspace,
            budget: 0,
            CancellationToken.None);

        Assert.Equal(identity.WorkspaceId, entered);
        Assert.Equal(identity.WorkspaceId, Assert.Single(job.Callers).Workspace);
        Assert.Equal(identity.UserId, job.Callers[0].Context.UserId);
    }

    /// <summary>
    /// The schedule does not choose a channel. A job that sends content to a model runs on the AI
    /// channel, where the per-project access policy and SB-18 apply; one that does not runs on the
    /// internal channel. Checked through the pass, so a schedule that passed its own idea of the
    /// channel would fail here.
    /// </summary>
    [Theory]
    [InlineData(WorkerModelUse.SendsContentToAModel, AccessChannel.Ai)]
    [InlineData(WorkerModelUse.None, AccessChannel.InternalSystem)]
    public async Task the_channel_is_the_one_the_job_declared(WorkerModelUse declared, AccessChannel expected)
    {
        RecordingJob job = new(declared);

        await WorkerSchedule.PassAsync(
            job,
            _ => Task.FromResult<MachineTokenIdentity?>(Identity()),
            _ => { },
            budget: 0,
            CancellationToken.None);

        Assert.Equal(expected, Assert.Single(job.Callers).Context.Channel);
    }

    /// <summary>
    /// Per pass, which is what makes <c>--budget</c> mean "the most one pass may send". A budget held
    /// for the process would let the first night spend it and every night after do nothing.
    /// </summary>
    [Fact]
    public async Task every_pass_gets_the_whole_budget()
    {
        RecordingJob job = new(WorkerModelUse.SendsContentToAModel, spendPerRun: 5);

        for (int pass = 0; pass < 3; pass++)
        {
            await WorkerSchedule.PassAsync(
                job,
                _ => Task.FromResult<MachineTokenIdentity?>(Identity()),
                _ => { },
                budget: 5,
                CancellationToken.None);
        }

        Assert.Equal([5, 5, 5], job.BudgetAtStart);
    }

    [Fact]
    public async Task the_first_pass_is_immediate()
    {
        int passes = 0;
        int passesBeforeFirstWait = -1;
        using CancellationTokenSource stopping = new();

        await WorkerSchedule.RunEveryAsync(
            "recording-job",
            _ =>
            {
                passes++;
                return Task.FromResult(WorkerRunReport.Completed("recording-job", 0));
            },
            TimeSpan.FromDays(1),
            (_, _) =>
            {
                passesBeforeFirstWait = passes;
                stopping.Cancel();
                return Task.CompletedTask;
            },
            new StringWriter(),
            new StringWriter(),
            stopping.Token);

        Assert.Equal(1, passesBeforeFirstWait);
    }

    [Fact]
    public async Task a_pass_that_throws_is_reported_and_the_schedule_continues()
    {
        int passes = 0;
        StringWriter errors = new();
        using CancellationTokenSource stopping = new();

        await WorkerSchedule.RunEveryAsync(
            "recording-job",
            _ =>
            {
                passes++;
                return passes == 1
                    ? throw new InvalidOperationException("the database restarted")
                    : Task.FromResult(WorkerRunReport.Completed("recording-job", 0));
            },
            TimeSpan.FromHours(1),
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
        Assert.Contains("the database restarted", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_cancelled_schedule_takes_no_further_pass()
    {
        int passes = 0;
        using CancellationTokenSource stopping = new();
        await stopping.CancelAsync();

        await WorkerSchedule.RunEveryAsync(
            "recording-job",
            _ =>
            {
                passes++;
                return Task.FromResult(WorkerRunReport.Completed("recording-job", 0));
            },
            TimeSpan.FromHours(1),
            (_, _) => Task.CompletedTask,
            new StringWriter(),
            new StringWriter(),
            stopping.Token);

        Assert.Equal(0, passes);
    }

    /// <summary>
    /// A refusal is an outcome — a revoked token, a database without pgvector — and a log somebody
    /// scans for errors should not light up for the system doing what it was told.
    /// </summary>
    [Fact]
    public void a_refused_pass_is_reported_as_an_outcome_with_its_reason()
    {
        StringWriter output = new();

        WorkerSchedule.Report(
            new RecordingJob(WorkerModelUse.None),
            WorkerRunReport.RefusedRun("recording-job", "the token was revoked"),
            output);

        Assert.Contains("refused: the token was revoked", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void the_console_offers_the_worker_command()
    {
        ParseResult parsed = CommandSurface.Build().Parse(
            ["worker", "record-embedding-sweep", "--every", "24h", "--budget", "0"], CommandSurface.Parsing);

        Assert.Empty(parsed.Errors);
        Assert.Equal("worker", parsed.CommandResult.Command.Name);
    }

    /// <summary>
    /// The same refusal the retention schedule makes: <c>--every 24</c> would otherwise be
    /// twenty-four days. Refused before a token is looked for or a container is built.
    /// </summary>
    [Fact]
    public async Task the_worker_refuses_an_interval_that_is_not_one()
    {
        ParseResult parsed = CommandSurface.Build().Parse(
            ["worker", "stale-record-sweep", "--every", "24", "--stale-after", "180d"], CommandSurface.Parsing);

        Assert.Equal(Runner.MisconfiguredExitCode, await parsed.InvokeAsync());
    }

    private static MachineTokenIdentity Identity() =>
        new(
            new MachineTokenId(Guid.NewGuid()),
            new UserId(Guid.NewGuid()),
            new WorkspaceId(Guid.NewGuid()),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30));

    /// <summary>Records what each run was handed, and spends what it is told to.</summary>
    private sealed class RecordingJob(WorkerModelUse modelUse, int spendPerRun = 0) : CallerBoundWorkerJob
    {
        public override string Name => "recording-job";

        public override WorkerModelUse ModelUse => modelUse;

        public List<WorkerCaller> Callers { get; } = [];

        public List<int> BudgetAtStart { get; } = [];

        public override Task<WorkerRunReport> RunAsync(
            WorkerCaller caller, WorkerBudget budget, CancellationToken cancellationToken)
        {
            Callers.Add(caller);
            BudgetAtStart.Add(budget.Remaining);

            if (spendPerRun > 0)
            {
                budget.TrySpend(spendPerRun);
            }

            return Task.FromResult(WorkerRunReport.Completed(Name, spendPerRun));
        }
    }
}
