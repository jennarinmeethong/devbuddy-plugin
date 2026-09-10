using System.Text.Json;
using DevBuddy.Application.Dispatch;
using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Workers.Jobs;

/// <summary>
/// The first job: finds knowledge nobody has touched in a while, in every project the worker's
/// membership actually reaches.
/// <para>
/// Chosen first on purpose, because it is the smallest job that exercises the whole shape and
/// carries none of the risk. It <b>calls no model</b> (<see cref="WorkerModelUse.None"/>), spends
/// nothing from the budget, writes nothing, publishes nothing, and answers a question `info.md`
/// asked for from the start: what has gone stale.
/// </para>
/// <para>
/// It reimplements nothing. <c>detect_staleness</c> has existed since Phase 6 and is the operation
/// that answers this; this job discovers which projects it may ask about and asks. A job that
/// queried the repository itself would be a second place for authorization, redaction and audit to
/// be got wrong, which is the reason the console's named commands are shortcuts over <c>run</c>
/// rather than handlers of their own.
/// </para>
/// <para>
/// Every call goes through <see cref="OperationDispatcher"/>, so the credential's workspace
/// ceiling, the live membership, the role, the secret scan and the audit entry all apply exactly
/// as they do to a person. If the worker's membership does not carry <c>ManageIndex</c>, every
/// project comes back refused and the report says so — which is the correct outcome, not a bug to
/// work around by widening the token.
/// </para>
/// </summary>
public sealed class StaleRecordSweepJob(OperationDispatcher dispatcher, TimeSpan staleAfter)
    : CallerBoundWorkerJob
{
    private readonly OperationDispatcher _dispatcher = Guard.NotNull(dispatcher, nameof(dispatcher));

    /// <summary>
    /// How long untouched is long enough to be suspect. An argument rather than a constant: a
    /// codebase under active work and an archive of finished projects do not agree on this, and a
    /// number this file chose would be wrong for one of them.
    /// </summary>
    private readonly TimeSpan _staleAfter = staleAfter > TimeSpan.Zero
        ? staleAfter
        : throw new ArgumentOutOfRangeException(
            nameof(staleAfter), "Staleness is measured in a positive duration.");

    public override string Name => "stale-record-sweep";

    /// <summary>
    /// Nothing here touches a model, so this runs on <c>AccessChannel.InternalSystem</c> and can
    /// reach <c>detect_staleness</c>, which is <c>AiExposure.Denied</c>. On the AI channel it
    /// could not: that channel is an allow-list of eighteen operations and this is not one of
    /// them. See <see cref="WorkerModelUse"/>.
    /// </summary>
    public override WorkerModelUse ModelUse => WorkerModelUse.None;

    public override async Task<WorkerRunReport> RunAsync(
        WorkerCaller caller, WorkerBudget budget, CancellationToken cancellationToken)
    {
        Guard.NotNull(caller, nameof(caller));
        Guard.NotNull(budget, nameof(budget));

        DispatchResult projects = await _dispatcher.InvokeAsync(
            "list_projects",
            Arguments(new { workspaceId = caller.Workspace.Value }),
            caller.Context,
            cancellationToken);

        if (!projects.IsSuccess)
        {
            // The ordinary case worth naming: the credential resolved, and the membership behind
            // it reaches nothing here. That is an answer, not a failure.
            return WorkerRunReport.RefusedRun(
                Name, $"Could not list projects in this workspace: {projects.Reason}");
        }

        Guid[] projectIds = [.. ProjectIdsIn(projects.Payload)];

        if (projectIds.Length == 0)
        {
            return WorkerRunReport.Completed(Name, callsSpent: 0);
        }

        List<StaleProjectSummary> summaries = [];

        foreach (Guid projectId in projectIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DispatchResult sweep = await _dispatcher.InvokeAsync(
                "detect_staleness",
                Arguments(new
                {
                    scope = new { workspaceId = caller.Workspace.Value, projectId },
                    staleAfter = _staleAfter,
                }),
                caller.Context,
                cancellationToken);

            summaries.Add(sweep.IsSuccess
                ? new StaleProjectSummary(projectId, FindingCountIn(sweep.Payload), null)
                : new StaleProjectSummary(projectId, 0, sweep.Reason));
        }

        Findings = summaries;

        // No provider calls, so nothing is spent. Reported as zero rather than omitted, because a
        // reader comparing jobs should be able to see which ones cost money and which do not.
        return WorkerRunReport.Completed(Name, callsSpent: 0);
    }

    /// <summary>
    /// What the last run found, per project. Held rather than returned so
    /// <see cref="WorkerRunReport"/> stays the same shape for every job; a caller that wants the
    /// detail reads it here.
    /// </summary>
    public IReadOnlyList<StaleProjectSummary> Findings { get; private set; } = [];

    private static JsonElement Arguments(object body) =>
        JsonSerializer.SerializeToElement(body, JsonConventions.Options);

    private static IEnumerable<Guid> ProjectIdsIn(JsonElement? payload)
    {
        if (payload is not { } value
            || !value.TryGetProperty("projects", out JsonElement projects)
            || projects.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement project in projects.EnumerateArray())
        {
            if (project.TryGetProperty("projectId", out JsonElement id)
                && id.TryGetGuid(out Guid parsed))
            {
                yield return parsed;
            }
        }
    }

    private static int FindingCountIn(JsonElement? payload) =>
        payload is { } value
        && value.TryGetProperty("findings", out JsonElement findings)
        && findings.ValueKind == JsonValueKind.Array
            ? findings.GetArrayLength()
            : 0;
}

/// <summary>
/// One project's result. <paramref name="Refusal"/> is filled in when the worker's membership does
/// not reach that project, which is information rather than an error: it tells an operator that
/// the token is narrower than the sweep they asked for.
/// </summary>
public sealed record StaleProjectSummary(Guid ProjectId, int StaleRecords, string? Refusal);
