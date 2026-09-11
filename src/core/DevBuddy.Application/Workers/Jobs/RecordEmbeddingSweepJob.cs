using System.Text.Json;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Dispatch;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Workers.Jobs;

/// <summary>
/// The job that fills the derived vector index (ADR-0012), and the only thing in this system that
/// writes one.
/// <para>
/// It declares <see cref="WorkerModelUse.SendsContentToAModel"/>, so it runs on the AI channel,
/// and that is not a formality — it is what makes the job safe. Three controls attach to that
/// channel and all three are load-bearing here:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>The per-project AI access policy.</b> <c>list_projects</c> on the AI channel omits a project
/// whose owner never enabled access, so this job cannot see it, cannot list its records, and
/// cannot send its text to a provider. A project nobody opened to AI is not embedded, which is the
/// single most important sentence about this file.
/// </item>
/// <item>
/// <b>SB-18.</b> Personal data is redacted out of the responses this job reads, so what it embeds
/// is what an AI caller was allowed to see.
/// </item>
/// <item>
/// <b>SB-17, twice.</b> The pipeline scans what it returns, and
/// <see cref="EmbeddingGateway"/> scans again before anything leaves the boundary. A record
/// carrying a credential is skipped with nothing sent.
/// </item>
/// </list>
/// <para>
/// <b>It embeds the published revision and nothing else.</b> A draft is not knowledge yet, and
/// indexing one would let a semantic search surface something nobody approved — the same mistake
/// as publishing on a stale approval, arrived at from a different direction.
/// </para>
/// <para>
/// <b>It re-embeds only what changed</b>, by comparing each published revision's content hash
/// against what the index already holds. That is the difference between a nightly sweep that costs
/// nothing on a quiet installation and one that re-bills the whole corpus every night.
/// </para>
/// </summary>
public sealed class RecordEmbeddingSweepJob(
    OperationDispatcher dispatcher, EmbeddingGateway gateway, IEmbeddingIndex index)
    : CallerBoundWorkerJob
{
    private readonly OperationDispatcher _dispatcher = Guard.NotNull(dispatcher, nameof(dispatcher));
    private readonly EmbeddingGateway _gateway = Guard.NotNull(gateway, nameof(gateway));
    private readonly IEmbeddingIndex _index = Guard.NotNull(index, nameof(index));

    public override string Name => "record-embedding-sweep";

    /// <summary>
    /// The AI channel, and the job would be wrong on any other. See the class remarks: the
    /// per-project access policy, SB-18, and the AI half of SB-17 all hang off this.
    /// </summary>
    public override WorkerModelUse ModelUse => WorkerModelUse.SendsContentToAModel;

    /// <summary>What the last run did, per project.</summary>
    public IReadOnlyList<EmbeddingSweepSummary> Summaries { get; private set; } = [];

    public override async Task<WorkerRunReport> RunAsync(
        WorkerCaller caller, WorkerBudget budget, CancellationToken cancellationToken)
    {
        Guard.NotNull(caller, nameof(caller));
        Guard.NotNull(budget, nameof(budget));

        // Both checks before anything is listed, so an installation that cannot index never reads
        // a record to discover that.
        if (!_gateway.IsConfigured || _gateway.Model is not { Length: > 0 } model)
        {
            return WorkerRunReport.RefusedRun(
                Name, "No embedding provider is configured, so there is nothing to index with.");
        }

        if (!await _index.IsAvailableAsync(cancellationToken))
        {
            return WorkerRunReport.RefusedRun(
                Name,
                "This database has no vector index: pgvector is not installed, so the migration "
                + "that would create it skipped itself.");
        }

        DispatchResult projects = await _dispatcher.InvokeAsync(
            "list_projects",
            Arguments(new { workspaceId = caller.Workspace.Value }),
            caller.Context,
            cancellationToken);

        if (!projects.IsSuccess)
        {
            return WorkerRunReport.RefusedRun(
                Name, $"Could not list projects in this workspace: {projects.Reason}");
        }

        List<EmbeddingSweepSummary> summaries = [];
        int spent = 0;

        foreach (Guid projectId in ProjectIdsIn(projects.Payload))
        {
            cancellationToken.ThrowIfCancellationRequested();

            EmbeddingSweepSummary summary = await SweepProjectAsync(
                caller, new ProjectScope(caller.Workspace, new ProjectId(projectId)), model, budget, cancellationToken);

            summaries.Add(summary);
            spent += summary.Embedded;
        }

        Summaries = summaries;
        return WorkerRunReport.Completed(Name, spent);
    }

    private async Task<EmbeddingSweepSummary> SweepProjectAsync(
        WorkerCaller caller,
        ProjectScope scope,
        string model,
        WorkerBudget budget,
        CancellationToken cancellationToken)
    {
        DispatchResult records = await _dispatcher.InvokeAsync(
            "list_records",
            Arguments(new { scope = new { workspaceId = scope.WorkspaceId.Value, projectId = scope.ProjectId.Value } }),
            caller.Context,
            cancellationToken);

        if (!records.IsSuccess)
        {
            // A project the credential reaches for listing but not for reading, or one whose AI
            // policy closed between the two calls. Reported, not thrown.
            return new EmbeddingSweepSummary(scope.ProjectId.Value, 0, 0, 0, records.Reason);
        }

        IReadOnlySet<string> alreadyIndexed =
            await _index.IndexedContentHashesAsync(scope, model, cancellationToken);

        int embedded = 0;
        int current = 0;
        int skipped = 0;

        foreach (Guid recordId in RecordIdsIn(records.Payload))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (budget.IsExhausted)
            {
                // Everything left is counted as skipped rather than silently dropped, so a run
                // that ran out says how much it did not get to.
                skipped++;
                continue;
            }

            PublishedRevision? published =
                await PublishedRevisionOfAsync(caller, scope, recordId, cancellationToken);

            if (published is null)
            {
                // No published revision, or the history could not be read. A draft is not
                // knowledge yet and is deliberately not indexed.
                skipped++;
                continue;
            }

            if (alreadyIndexed.Contains(published.ContentHash))
            {
                current++;
                continue;
            }

            if (!await EmbedAsync(caller, scope, model, recordId, published, budget, cancellationToken))
            {
                skipped++;
                continue;
            }

            embedded++;
        }

        return new EmbeddingSweepSummary(scope.ProjectId.Value, embedded, current, skipped, null);
    }

    /// <summary>
    /// Reads one record and writes its vector, or answers false and writes nothing.
    /// <para>
    /// False covers a refused read, a refused embedding — which includes a record carrying a
    /// credential, blocked by SB-17 with nothing sent — and a budget that will not cover it. All
    /// three are ordinary, and none of them should stop the sweep.
    /// </para>
    /// </summary>
    private async Task<bool> EmbedAsync(
        WorkerCaller caller,
        ProjectScope scope,
        string model,
        Guid recordId,
        PublishedRevision published,
        WorkerBudget budget,
        CancellationToken cancellationToken)
    {
        DispatchResult record = await _dispatcher.InvokeAsync(
            "get_record",
            Arguments(new
            {
                scope = new { workspaceId = scope.WorkspaceId.Value, projectId = scope.ProjectId.Value },
                recordId,
                revisionNumber = published.Number,
            }),
            caller.Context,
            cancellationToken);

        if (!record.IsSuccess || record.Payload is not { } payload)
        {
            return false;
        }

        string text = TextToEmbed(payload);

        if (text.Length == 0)
        {
            return false;
        }

        EmbeddingOutcome outcome = await _gateway.EmbedAsync(
            caller.Context, [text], budget, cancellationToken);

        if (outcome.Refused || outcome.Vectors.Count != 1)
        {
            return false;
        }

        await _index.UpsertAsync(
            scope,
            model,
            [new EmbeddedRevision(
                new KnowledgeRecordId(recordId),
                published.Number,
                published.ContentHash,
                outcome.Vectors[0])],
            cancellationToken);

        return true;
    }

    /// <summary>
    /// The published revision's number and content hash, from <c>view_record_history</c>.
    /// <para>
    /// The history is read rather than the record, because it carries the content hash and the
    /// record does not — and the hash is what lets an unchanged revision be skipped without
    /// paying to embed it again.
    /// </para>
    /// </summary>
    private async Task<PublishedRevision?> PublishedRevisionOfAsync(
        WorkerCaller caller, ProjectScope scope, Guid recordId, CancellationToken cancellationToken)
    {
        DispatchResult history = await _dispatcher.InvokeAsync(
            "view_record_history",
            Arguments(new
            {
                scope = new { workspaceId = scope.WorkspaceId.Value, projectId = scope.ProjectId.Value },
                recordId,
            }),
            caller.Context,
            cancellationToken);

        if (!history.IsSuccess
            || history.Payload is not { } payload
            || !payload.TryGetProperty("revisions", out JsonElement revisions)
            || revisions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement revision in revisions.EnumerateArray())
        {
            if (!revision.TryGetProperty("isPublished", out JsonElement isPublished)
                || isPublished.ValueKind != JsonValueKind.True)
            {
                continue;
            }

            if (revision.TryGetProperty("revisionNumber", out JsonElement number)
                && number.TryGetInt32(out int parsed)
                && revision.TryGetProperty("contentHash", out JsonElement hash)
                && hash.GetString() is { Length: > 0 } contentHash)
            {
                return new PublishedRevision(parsed, contentHash);
            }
        }

        return null;
    }

    /// <summary>
    /// Title and body, joined. Both, because a record whose title carries the subject and whose
    /// body carries the reasoning is badly served by embedding either alone.
    /// </summary>
    private static string TextToEmbed(JsonElement record)
    {
        string title = record.TryGetProperty("title", out JsonElement value)
            ? value.GetString() ?? string.Empty
            : string.Empty;

        string body = record.TryGetProperty("body", out JsonElement text)
            ? text.GetString() ?? string.Empty
            : string.Empty;

        return string.Join("\n\n", new[] { title, body }.Where(part => part.Length > 0));
    }

    private static JsonElement Arguments(object body) =>
        JsonSerializer.SerializeToElement(body, JsonConventions.Options);

    private static IEnumerable<Guid> ProjectIdsIn(JsonElement? payload) =>
        IdentifiersIn(payload, "projects", "projectId");

    private static IEnumerable<Guid> RecordIdsIn(JsonElement? payload) =>
        IdentifiersIn(payload, "records", "recordId");

    private static IEnumerable<Guid> IdentifiersIn(
        JsonElement? payload, string arrayName, string propertyName)
    {
        if (payload is not { } value
            || !value.TryGetProperty(arrayName, out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.TryGetProperty(propertyName, out JsonElement id) && id.TryGetGuid(out Guid parsed))
            {
                yield return parsed;
            }
        }
    }

    private sealed record PublishedRevision(int Number, string ContentHash);
}

/// <summary>
/// What one project's sweep did. <paramref name="AlreadyCurrent"/> is the number that cost
/// nothing, which on a quiet installation should be nearly all of them —
/// <paramref name="Embedded"/> staying high every night means the hash comparison is not working.
/// </summary>
public sealed record EmbeddingSweepSummary(
    Guid ProjectId,
    int Embedded,
    int AlreadyCurrent,
    int Skipped,
    string? Refusal);
