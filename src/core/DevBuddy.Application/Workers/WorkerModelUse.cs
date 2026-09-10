namespace DevBuddy.Application.Workers;

/// <summary>
/// Whether a job sends content to a model, which is what decides its channel.
/// <para>
/// This exists because of a contradiction inside ADR-0013 that only appeared when the first job
/// was written. The ADR says a caller-bound worker is "bounded by it exactly like any other
/// caller", and the first reading of that put every worker on <c>AccessChannel.Ai</c> — the
/// strictest setting, and the only one where the per-project AI access policy and the SB-18
/// personal-data redaction apply.
/// </para>
/// <para>
/// But the AI channel is also an <b>allow-list of eighteen operations</b>, and every feature the
/// ADR's own list names — scheduled source analysis, stale-record detection, embedding
/// generation — needs <c>ManageIndex</c> or <c>ManageSources</c>, both of which are
/// <c>AiExposure.Denied</c>. A worker pinned to the AI channel can search, read, analyse and
/// create a draft, and nothing else. It cannot do the work it was proposed for.
/// </para>
/// <para>
/// The resolution is that <b>the channel follows whether the job touches a model, not whether it
/// is a worker.</b> A job that sends content to an LLM or an embedding provider must be on the AI
/// channel, because that is where the controls protecting that act live. A job that touches no
/// model has nothing for those controls to protect and runs on
/// <c>AccessChannel.InternalSystem</c> — still holding a real machine token, still bounded by a
/// live membership, a role, and the credential's workspace ceiling. What it is not subject to are
/// AI-specific rules about a model it never calls.
/// </para>
/// <para>
/// The declaration lives on the job type rather than on a call, so it cannot be chosen per run.
/// And a job that declared <see cref="None"/> and then reached for a model is refused: the
/// embedding gateway takes a caller and rejects one that is not on the AI channel, so the lie
/// costs the job its embedding rather than costing the installation its policy.
/// </para>
/// </summary>
public enum WorkerModelUse
{
    /// <summary>
    /// Sends nothing to a model or an embedding provider. Runs on
    /// <c>AccessChannel.InternalSystem</c>, and may therefore reach the human-only operations its
    /// membership carries — which is what makes a stale-record sweep or a source refresh possible
    /// at all.
    /// </summary>
    None = 1,

    /// <summary>
    /// Sends content to an LLM or an embedding provider. Runs on <c>AccessChannel.Ai</c>, so the
    /// eighteen-operation allow-list, the per-project AI access policy and the SB-18 redaction all
    /// apply, exactly as they do to Claude and Codex.
    /// </summary>
    SendsContentToAModel = 2,
}
