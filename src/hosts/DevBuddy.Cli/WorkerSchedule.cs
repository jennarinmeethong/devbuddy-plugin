using System.Globalization;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Workers;
using DevBuddy.Application.Workers.Jobs;
using DevBuddy.Domain.Common;

namespace DevBuddy.Cli;

/// <summary>
/// One worker pass, and the loop that repeats it: the schedule ADR-0013 said belongs beside the
/// retention service, off unless configured.
/// <para>
/// A scheduling mode on the console for the reason <see cref="RetentionSchedule"/> gives — the
/// images are chiseled, so there is no shell to put <c>cron</c> in — and it differs from that
/// schedule in the one way that matters. <b>Retention runs as nobody. A worker runs as
/// somebody.</b> Both jobs are <see cref="CallerBoundWorkerJob"/>s: they read project content,
/// which a person's permissions gate, so ADR-0013's second shape is closed to them and the first is
/// the only one left. Every pass acts as the owner of a real machine token, through the ordinary
/// pipeline, and the audit trail names that owner.
/// </para>
/// <para>
/// <b>The token is resolved on every pass, not once at start-up.</b> A token resolves per run and
/// can be revoked between two of them, and a schedule that resolved it once would keep working for
/// as long as the container stayed up after somebody took its access away. Revoking the token on
/// the Teams screen is how an administrator stops a worker, and that has to take effect at the next
/// pass rather than at the next deployment.
/// </para>
/// <para>
/// So is the budget. It is per pass, which is what makes <c>--budget</c> mean "the most one pass
/// may send" rather than "the most the process may send before it goes quiet forever".
/// </para>
/// </summary>
internal static class WorkerSchedule
{
    /// <summary>
    /// Where the token comes from. Its own name rather than the plugins' <c>DEVBUDDY_TOKEN</c>,
    /// because the two are different credentials held for different reasons: a plugin token is a
    /// person's, used from their own machine; this one is a deployment's, and an operator reading a
    /// Compose file should be able to tell which is which without tracing where it came from.
    /// </summary>
    public const string TokenVariable = "DEVBUDDY_WORKER_TOKEN";

    public const string StaleRecordSweep = "stale-record-sweep";

    public const string RecordEmbeddingSweep = "record-embedding-sweep";

    /// <summary>
    /// The jobs a schedule may name. Two, because there are two; a third is added here on purpose
    /// or not at all, never by reflection over whatever happens to derive from a base class.
    /// </summary>
    public static IReadOnlyList<string> JobNames { get; } = [StaleRecordSweep, RecordEmbeddingSweep];

    /// <summary>
    /// Refuses a configuration that would run but not do what it looks like it does, before
    /// anything is built.
    /// <para>
    /// The embedding sweep has to be told its budget. A default that spent would be a cost decision
    /// this file took for somebody, and a default of zero would be a schedule that ran every night
    /// and embedded nothing while looking configured. Zero stays accepted when it is written out,
    /// because that is how an operator switches spending off without removing the service.
    /// </para>
    /// <para>
    /// A flag the chosen job would ignore is refused rather than tolerated, for the same reason
    /// <c>--every 24</c> is: a setting that is silently ignored reads as a setting that works.
    /// </para>
    /// </summary>
    public static bool TryValidate(
        string job, int? budget, TimeSpan? staleAfter, string? token, out string? problem)
    {
        problem = null;

        if (!JobNames.Contains(job, StringComparer.Ordinal))
        {
            problem =
                $"'{job}' is not a worker job. There are two: {string.Join(" and ", JobNames)}.";

            return false;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            problem =
                $"No worker token. Set {TokenVariable} to a machine token minted for the account "
                + "this worker acts as, in the workspace it works in. A worker runs as somebody or "
                + "not at all.";

            return false;
        }

        if (budget is < 0)
        {
            problem = "--budget cannot be negative. Zero is how spending is switched off.";
            return false;
        }

        if (job == RecordEmbeddingSweep)
        {
            if (budget is null)
            {
                problem =
                    "record-embedding-sweep sends record text to a model and needs --budget: the "
                    + "most texts one pass may embed. Zero is accepted and embeds nothing.";

                return false;
            }

            if (staleAfter is not null)
            {
                problem =
                    "--stale-after belongs to stale-record-sweep. record-embedding-sweep would "
                    + "ignore it.";

                return false;
            }

            return true;
        }

        if (staleAfter is null)
        {
            problem =
                "stale-record-sweep needs --stale-after, for example 180d. How long untouched is "
                + "suspect is the installation's call: an active codebase and an archive of "
                + "finished projects do not agree on it.";

            return false;
        }

        if (budget is > 0)
        {
            problem =
                "stale-record-sweep calls no model and spends nothing, so --budget would be "
                + "ignored. Leave it out.";

            return false;
        }

        return true;
    }

    /// <summary>
    /// Runs one pass as whoever the token names today.
    /// <para>
    /// A token that resolves to nobody — unknown, revoked, expired, or issued before tokens carried
    /// a workspace — refuses the pass with nothing read. Never a fallback to another identity:
    /// <see cref="WorkerCaller.FromMachineToken"/> answers null for exactly that reason, and this
    /// is where the null is honoured.
    /// </para>
    /// <para>
    /// The workspace entered is the token's, taken from the resolved credential. Nothing a job was
    /// configured with can name a different one.
    /// </para>
    /// </summary>
    /// <param name="resolveToken">
    /// Resolves the configured token against the store. A delegate so a test can revoke it between
    /// two passes.
    /// </param>
    /// <param name="enterWorkspace">Sets the tenant context the persistence filters read.</param>
    public static async Task<WorkerRunReport> PassAsync(
        CallerBoundWorkerJob job,
        Func<CancellationToken, Task<MachineTokenIdentity?>> resolveToken,
        Action<WorkspaceId> enterWorkspace,
        int budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(resolveToken);
        ArgumentNullException.ThrowIfNull(enterWorkspace);

        MachineTokenIdentity? identity = await resolveToken(cancellationToken);

        WorkerCaller? caller = WorkerCaller.FromMachineToken(
            identity, Guid.NewGuid().ToString("N"), job.ModelUse);

        if (caller is null)
        {
            return WorkerRunReport.RefusedRun(
                job.Name,
                "The worker token resolved to nobody: it is unknown, revoked, expired, or was "
                + "issued before tokens carried a workspace. Nothing was read.");
        }

        enterWorkspace(caller.Workspace);

        // A fresh budget per pass. See the class remarks.
        return await job.RunAsync(caller, new WorkerBudget(budget), cancellationToken);
    }

    /// <summary>
    /// Writes what a pass did. A refusal is written to the ordinary output rather than to the error
    /// stream, because it is an outcome — a revoked token, a project nobody opened to AI, a
    /// database without pgvector — and a log somebody scans for errors should not light up for the
    /// system doing what it was told.
    /// </summary>
    public static void Report(CallerBoundWorkerJob job, WorkerRunReport report, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(output);

        if (report.Refused)
        {
            output.WriteLine($"{report.JobName} refused: {report.Reason}");
            return;
        }

        output.WriteLine(
            $"{report.JobName} completed; {report.CallsSpent} text(s) sent to a model");

        switch (job)
        {
            case StaleRecordSweepJob stale:
                foreach (StaleProjectSummary finding in stale.Findings)
                {
                    output.WriteLine(finding.Refusal is null
                        ? $"  project {finding.ProjectId}  stale records {finding.StaleRecords}"
                        : $"  project {finding.ProjectId}  refused: {finding.Refusal}");
                }

                break;

            case RecordEmbeddingSweepJob sweep:
                foreach (EmbeddingSweepSummary summary in sweep.Summaries)
                {
                    output.WriteLine(summary.Refusal is null
                        ? $"  project {summary.ProjectId}  embedded {summary.Embedded}  "
                          + $"already current {summary.AlreadyCurrent}  skipped {summary.Skipped}  "
                          + $"removed {summary.Removed}"
                        : $"  project {summary.ProjectId}  refused: {summary.Refusal}");
                }

                break;
        }
    }

    /// <summary>
    /// Runs a pass immediately and then every <paramref name="interval"/> until cancelled.
    /// <para>
    /// The same three decisions the retention schedule made, for the same reasons: the first pass
    /// is immediate so a working schedule is distinguishable from a broken one on the day it is
    /// deployed; a pass that throws is reported and the loop continues, because a database that is
    /// briefly unreachable is ordinary for a process meant to run for months; and a cancelled
    /// schedule takes no further pass. A <b>refused</b> pass is not a failure at all and needs no
    /// handling here — its report says why, and the next pass tries again, which is what lets a
    /// token that was revoked by mistake be reissued without restarting anything.
    /// </para>
    /// <para>
    /// Kept apart from <see cref="RetentionSchedule.SweepEveryAsync"/> rather than sharing its loop.
    /// That loop is the tested half of a control (SB-27) that runs as nobody, and this one runs as
    /// somebody; a shared loop is one edit away from making a change to either quietly a change to
    /// both.
    /// </para>
    /// </summary>
    /// <param name="wait">A seam, so a test can drive a year of schedule without spending it.</param>
    public static async Task RunEveryAsync(
        string jobName,
        Func<CancellationToken, Task<WorkerRunReport>> pass,
        TimeSpan interval,
        Func<TimeSpan, CancellationToken, Task> wait,
        TextWriter output,
        TextWriter errors,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(wait);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        output.WriteLine(
            $"Running {jobName} now and every "
            + $"{interval.ToString("c", CultureInfo.InvariantCulture)}. "
            + "Stop the process to stop the schedule; revoke the token to stop the work.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await pass(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                errors.WriteLine(
                    $"A {jobName} pass failed and the schedule continues: "
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

        output.WriteLine($"The {jobName} schedule stopped.");
    }
}
