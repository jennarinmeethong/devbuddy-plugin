namespace DevBuddy.Application.Workers;

/// <summary>
/// Autonomous background work, in the only two shapes ADR-0013 permits.
/// <para>
/// The problem this solves is the one that removed <c>restore_system</c> as an operation and kept
/// <c>retention</c> out of the pipeline: <b>a background job has no caller to authorise it
/// against.</b> Two answers are sound and everything between them is not, so the two answers are
/// types and there is no third base class to inherit from.
/// </para>
/// <list type="number">
/// <item>
/// <see cref="CallerBoundWorkerJob"/> — the job acts as somebody. It is handed a
/// <see cref="WorkerCaller"/>, which can only be produced by resolving a real machine token, and
/// every call it makes goes through the ordinary pipeline: the credential's workspace ceiling,
/// live membership, role, per-project AI policy, the secret scan, the redactor, and the audit
/// entry naming its owner.
/// </item>
/// <item>
/// <see cref="InstallationWorkerJob"/> — the job acts as nobody. It gets no caller, and may
/// therefore touch <b>nothing a person's permissions would gate</b>: installation-wide
/// maintenance only, the same standing <c>retention</c> has.
/// </item>
/// </list>
/// <para>
/// A job outside the pipeline that read project content would be an installation-wide superuser
/// running unattended, which is worse than one a person has to invoke. `WorkerAuthorizationTests`
/// enforces the second shape's reach by allow-list rather than by this comment.
/// </para>
/// <para>
/// Nothing here schedules anything. A schedule is the host's business and belongs beside the
/// retention service in <c>docker/compose.yaml</c>, off unless configured, exactly as telemetry,
/// SMTP and the GitHub API mode are.
/// </para>
/// </summary>
public interface IWorkerJob
{
    /// <summary>
    /// What this job is called, for a log line and an operator's schedule. Not an identity: it
    /// authorises nothing and is never read by the pipeline.
    /// </summary>
    string Name { get; }
}

/// <summary>
/// Shape one: a job bounded by a credential, exactly like any other caller.
/// <para>
/// The caller arrives as an argument rather than as a constructor dependency, because a token
/// resolves per run and can be revoked between two of them. A job that captured its caller once
/// would keep working for the rest of the process's life after somebody revoked it.
/// </para>
/// </summary>
public abstract class CallerBoundWorkerJob : IWorkerJob
{
    public abstract string Name { get; }

    /// <summary>
    /// Runs once, as <paramref name="caller"/>, spending from <paramref name="budget"/>.
    /// <para>
    /// Implementations call use cases through the ordinary executor or dispatcher. They must not
    /// reach a port directly: doing so is what skipping authorization, redaction and audit looks
    /// like from inside a background job, and it is the same defect as a host reaching past the
    /// pipeline.
    /// </para>
    /// </summary>
    public abstract Task<WorkerRunReport> RunAsync(
        WorkerCaller caller, WorkerBudget budget, CancellationToken cancellationToken);
}

/// <summary>
/// Shape two: a job with no caller, and therefore no reach into anything a permission gates.
/// <para>
/// Sweeps, index rebuilds, counting things. It reads no record content into an output anybody
/// sees, it creates no draft, and it publishes nothing. The reason it may run unattended is
/// precisely that there is nothing for a permission to protect it from.
/// </para>
/// </summary>
public abstract class InstallationWorkerJob : IWorkerJob
{
    public abstract string Name { get; }

    public abstract Task<WorkerRunReport> RunAsync(
        WorkerBudget budget, CancellationToken cancellationToken);
}

/// <summary>
/// What one run did. <paramref name="Refused"/> is a first-class outcome rather than an error: a
/// run that stopped because its budget was gone, or because its credential no longer resolves, did
/// the right thing and should not read as a failure in a log somebody scans.
/// </summary>
public sealed record WorkerRunReport(
    string JobName,
    int CallsSpent,
    bool Refused,
    string? Reason = null)
{
    public static WorkerRunReport Completed(string jobName, int callsSpent) =>
        new(jobName, callsSpent, Refused: false);

    public static WorkerRunReport RefusedRun(string jobName, string reason) =>
        new(jobName, 0, Refused: true, reason);
}
