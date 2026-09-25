namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Turns text into a vector. The port ADR-0012 settles, in the shape the project owner confirmed
/// on 2026-09-10: one port, two modes, and nothing registered when neither is configured.
/// <para>
/// <b>Do not call this directly.</b> Every legitimate call goes through
/// <c>EmbeddingGateway</c>, which is the only place the SB-17 scan happens before text leaves and
/// the only place the channel is checked. An architecture test fails the build if anything else in
/// the product references this interface, for the same reason a host may not reach past the
/// use-case pipeline: a second entry point is a second place for the controls to be missing.
/// </para>
/// <para>
/// The two modes are not equivalent, and the difference is the subject of ADR-0012 rather than a
/// deployment detail. A self-hosted model in the stack sends nothing out of the trust boundary. A
/// hosted API is a third egress path beside the AI channel and the telemetry exporter, and the
/// only one that leaves a copy of the text in somebody else's system by design — so enabling it
/// takes the vendor named, its host in <c>OutboundAccess:AllowedHosts</c>, and an acceptance in
/// `info.md` of its own.
/// </para>
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// What this provider is, for a health report and a log line. Names the mode and the model,
    /// never a key.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// The dimension every vector from this provider has. Recorded with the index, because an
    /// index built at one dimension and queried at another returns nonsense rather than an error.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Whether this provider sends text outside the trust boundary. True for a hosted API, false
    /// for a model in the stack.
    /// <para>
    /// Asked rather than inferred from the mode, so the gateway's egress rules read as what they
    /// are instead of as a switch on a configuration enum.
    /// </para>
    /// </summary>
    bool LeavesTheBoundary { get; }

    /// <summary>
    /// The text that is sent for <paramref name="text"/> embedded as <paramref name="purpose"/>
    /// (Phase 14, C1). Some models are trained to be told that a text is a query, which a document
    /// never is. What that looks like belongs to the model, so the provider says it. By default
    /// the text is sent as it is.
    /// <para>
    /// The gateway calls this <b>before</b> it scans. SB-17 scans the text as sent, instruction
    /// included, and a provider must not change a text again inside <see cref="EmbedAsync"/>.
    /// </para>
    /// </summary>
    string TextFor(string text, EmbeddingPurpose purpose) => text;

    Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

/// <summary>
/// What an embedded text is for: a <see cref="Document"/> the index keeps, or a
/// <see cref="Query"/> compared against it. It does not name a vendor. A model that treats the
/// two alike needs no instruction, and then both are sent unchanged.
/// </summary>
public enum EmbeddingPurpose
{
    /// <summary>A published record, or a chunk of one, written into the index.</summary>
    Document = 1,

    /// <summary>A question asked of the index, never written into it.</summary>
    Query = 2,
}

/// <summary>
/// The vectors, in the order the texts were given, plus what the call cost in provider terms.
/// <para>
/// <paramref name="Vectors"/> has one entry per input. A provider that answered with fewer is a
/// provider that silently dropped an input, and the gateway refuses that rather than writing an
/// index whose rows are misaligned with the records they claim to describe.
/// </para>
/// </summary>
public sealed record EmbeddingResult(
    IReadOnlyList<ReadOnlyMemory<float>> Vectors, int CallsMade);
