using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Workers;

/// <summary>
/// The only way to reach an <see cref="IEmbeddingProvider"/>, and the place ADR-0012's rules are
/// applied rather than described.
/// <para>
/// ADR-0012's first point is that embedding text is <b>egress</b>: a third path out of the trust
/// boundary beside the AI channel and the telemetry exporter, and the only one that leaves a copy
/// of the text in somebody else's system by design. So SB-17 and SB-18 have to apply <i>before</i>
/// text leaves, not to what comes back. A vector is not reversible into its input in any useful
/// way, which is exactly why scanning the response would be scanning the wrong copy.
/// </para>
/// <para>
/// Four refusals, in this order, and each of them has a reason that is not "defence in depth":
/// </para>
/// <list type="number">
/// <item>
/// <b>No provider configured</b> — the default. An installation that has chosen no provider gets a
/// refusal, never a silent no-op that leaves a caller believing an index exists.
/// </item>
/// <item>
/// <b>A caller on <see cref="AccessChannel.InternalSystem"/></b> — which is what stops a job that
/// declared <see cref="WorkerModelUse.None"/> from reaching a model anyway. Declaring no model use
/// buys a job the human-only operations its membership carries; it does not also buy it the model.
/// <para>
/// Refusing that one channel rather than requiring the AI channel, and the difference is not
/// cosmetic. The first version of this required <see cref="AccessChannel.Ai"/>, which enforced the
/// worker rule correctly and locked <b>people</b> out — a person searching their own project from
/// the web interface is on <see cref="AccessChannel.Human"/>, and semantic search would have been
/// an AI-only feature by accident. <c>InternalSystem</c> is the exact channel the rule is about:
/// no host puts a human or a model on it, and a worker only lands there by declaring it touches no
/// model.
/// </para>
/// </item>
/// <item>
/// <b>A secret in the text</b> — refused with nothing sent, the same shape a draft carrying a
/// credential gets (SB-17). The scan happens here because here is before the call; a provider that
/// scanned its own input would be a control living inside the thing it constrains.
/// </item>
/// <item>
/// <b>A short answer</b> — a provider returning fewer vectors than it was given texts has dropped
/// an input, and writing that into an index would misalign every row after it against the record
/// it claims to describe. Refused rather than trimmed.
/// </item>
/// </list>
/// <para>
/// What is deliberately <b>not</b> here: the personal-data check. SB-18 is applied by
/// <c>UseCaseExecutor</c> on the AI channel when a project's policy carries no approved bounded
/// scope, and the text this gateway embeds arrives from a use-case response that has already been
/// through it. Repeating it would be a second implementation of a policy that has one, and the two
/// would drift. What this gateway must never do is accept text from anywhere but a use-case
/// response, which is why it takes a caller: there is no path to it that has not been authorised.
/// </para>
/// </summary>
public sealed class EmbeddingGateway(ISecretScanner scanner, IEmbeddingProvider? provider = null)
{
    private readonly ISecretScanner _scanner = Guard.NotNull(scanner, nameof(scanner));
    private readonly IEmbeddingProvider? _provider = provider;

    /// <summary>
    /// The model behind the configured provider, or null when there is none. The index keys rows
    /// by it, because vectors from two models are not comparable.
    /// </summary>
    public string? Model => _provider is null ? null : ModelOf(_provider.Description);

    /// <summary>
    /// True when this installation has a provider. False is the default and not a fault: full-text
    /// search is what v1 shipped and remains complete on its own.
    /// </summary>
    public bool IsConfigured => _provider is not null;

    /// <summary>What the configured provider is, or null. Names a mode and a model, never a key.</summary>
    public string? ProviderDescription => _provider?.Description;

    /// <summary>
    /// Embeds <paramref name="texts"/> on behalf of <paramref name="caller"/>, or refuses and
    /// sends nothing.
    /// </summary>
    /// <param name="budget">
    /// A worker run's remaining provider calls, or null for a caller that is not a worker.
    /// <para>
    /// Null is not "unlimited". An interactive search is one call per query and is bounded by the
    /// request rate limiter the API already applies (SB-21); a worker loop is the thing that needs
    /// counting, because nobody is watching it and it can spend all night.
    /// </para>
    /// </param>
    public async Task<EmbeddingOutcome> EmbedAsync(
        CallerContext caller,
        IReadOnlyList<string> texts,
        WorkerBudget? budget,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(caller, nameof(caller));
        Guard.NotNull(texts, nameof(texts));

        if (_provider is null)
        {
            return EmbeddingOutcome.Refuse(
                "This installation has no embedding provider configured, so there is nothing to "
                + "embed with. Full-text search is unaffected.");
        }

        if (caller.Channel == AccessChannel.InternalSystem)
        {
            // A job that declared it touches no model, now reaching for one. Refused here rather
            // than trusted, because that declaration is what bought it the human-only operations
            // its membership carries.
            return EmbeddingOutcome.Refuse(
                $"Embedding is refused on the {AccessChannel.InternalSystem} channel, which is "
                + $"where a job that declared {WorkerModelUse.None} runs. A job that needs a model "
                + $"declares {WorkerModelUse.SendsContentToAModel} and is bounded by the AI "
                + "channel's controls accordingly.");
        }

        if (texts.Count == 0)
        {
            return EmbeddingOutcome.Embedded([], 0);
        }

        foreach (string text in texts)
        {
            SecretScanResult scan = await _scanner.ScanAsync(text ?? string.Empty, cancellationToken);

            if (scan.HasFindings)
            {
                // The rule names, never the matched text: a refusal message full of real
                // credentials is worse than the problem it reports.
                return EmbeddingOutcome.Blocked(
                    [.. scan.Findings.Select(finding => finding.RuleName).Distinct(StringComparer.Ordinal)]);
            }
        }

        // Budget after the scan and before the call, because a refused scan should cost nothing.
        if (budget is not null && !budget.TrySpend())
        {
            return EmbeddingOutcome.Refuse(
                $"The run's budget of {budget.MaximumCalls} provider call(s) is spent.");
        }

        EmbeddingResult result = await _provider.EmbedAsync(texts, cancellationToken);

        if (result.Vectors.Count != texts.Count)
        {
            return EmbeddingOutcome.Refuse(
                $"The provider returned {result.Vectors.Count} vector(s) for {texts.Count} text(s). "
                + "An index built from a short answer is misaligned against the records it "
                + "describes, so nothing is written.");
        }

        return EmbeddingOutcome.Embedded(result.Vectors, result.CallsMade);
    }

    /// <summary>
    /// The model name out of a provider's description, which is where it already has to be for a
    /// health report. Read rather than added as a second property, so a provider cannot report one
    /// model to an operator and another to the index.
    /// </summary>
    private static string? ModelOf(string description)
    {
        const string marker = "model ";
        int at = description.IndexOf(marker, StringComparison.Ordinal);

        return at < 0 ? null : description[(at + marker.Length)..].Trim();
    }
}

/// <summary>
/// What one embedding call did. Three outcomes and not an exception for any of them: a refusal and
/// a block are ordinary answers a job has to handle, and a job that saw them as exceptions would
/// be a job whose logs read as failures when it behaved correctly.
/// </summary>
public sealed record EmbeddingOutcome
{
    private EmbeddingOutcome()
    {
    }

    public IReadOnlyList<ReadOnlyMemory<float>> Vectors { get; private init; } = [];

    public int CallsMade { get; private init; }

    /// <summary>True when nothing was embedded and nothing was sent.</summary>
    public bool Refused { get; private init; }

    /// <summary>True when the text carried something SB-17 will not let out. Also a refusal.</summary>
    public bool BlockedBySecretScan { get; private init; }

    /// <summary>The scanner rules that matched, if any. Rule names only, never matched text.</summary>
    public IReadOnlyList<string> BlockingRules { get; private init; } = [];

    public string? Reason { get; private init; }

    internal static EmbeddingOutcome Embedded(
        IReadOnlyList<ReadOnlyMemory<float>> vectors, int callsMade) =>
        new() { Vectors = vectors, CallsMade = callsMade };

    internal static EmbeddingOutcome Refuse(string reason) =>
        new() { Refused = true, Reason = reason };

    internal static EmbeddingOutcome Blocked(IReadOnlyList<string> rules) =>
        new()
        {
            Refused = true,
            BlockedBySecretScan = true,
            BlockingRules = rules,
            Reason =
                "The text carries something that must not leave the boundary: "
                + string.Join(", ", rules) + ". Nothing was sent.",
        };
}
