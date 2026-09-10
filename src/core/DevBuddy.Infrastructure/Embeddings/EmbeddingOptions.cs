namespace DevBuddy.Infrastructure.Embeddings;

/// <summary>
/// Which embedding provider, if any. <see cref="None"/> is the default and means the port is not
/// registered at all — the same posture telemetry, SMTP and the GitHub API mode take.
/// </summary>
public enum EmbeddingProviderKind
{
    /// <summary>
    /// No provider. Full-text search and structured filters are what v1 shipped and remain
    /// complete on their own; an installation that wants none of this pays nothing and exports
    /// nothing.
    /// </summary>
    None = 1,

    /// <summary>
    /// A model served inside the deployment. <b>No text leaves the trust boundary</b>, which is
    /// the whole reason this mode exists as a distinct choice rather than as a different URL.
    /// </summary>
    SelfHosted = 2,

    /// <summary>
    /// A vendor's API. <b>Text leaves the boundary</b> — a third egress path beside the AI channel
    /// and the telemetry exporter, and the only one that leaves a copy in somebody else's system
    /// by design. Refuses to start unless its host is on the outbound allow-list; see
    /// <see cref="EmbeddingOptions"/>.
    /// </summary>
    HostedApi = 3,
}

/// <summary>
/// How this installation embeds text, per ADR-0012 as amended on 2026-09-10: one port, two modes,
/// off by default.
/// <para>
/// Spelled as an enum rather than inferred from whether an endpoint is set, for the reason
/// <c>docker/compose.yaml</c> gives about the email provider: a mode that appears when a URL
/// happens to be present is a mode nobody chose, and the difference between these two is whether
/// project text leaves the building.
/// </para>
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    public EmbeddingProviderKind Provider { get; set; } = EmbeddingProviderKind.None;

    /// <summary>
    /// Where the provider is. For <see cref="EmbeddingProviderKind.SelfHosted"/> this is operator
    /// configuration pointing inside the deployment, in the same way
    /// <c>Evidence:ServiceUrl</c> points at the bundled object store; it is not a URL derived from
    /// analysed content, so it does not pass through <c>UrlGuard</c> and does not need
    /// <c>OutboundAccess:AllowPrivateAddresses</c>.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>The model to ask for. Recorded with the index, because a vector is meaningless without it.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// The credential for a hosted API. Never logged and never included in a health report; the
    /// provider's description names the mode and the model and stops there.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// How many numbers a vector has. Required, because an index built at one dimension and
    /// queried at another returns nonsense rather than an error, and no default this file picked
    /// would be right for a model an operator chose.
    /// </summary>
    public int Dimensions { get; set; }

    /// <summary>
    /// How many texts to send per call. A batch is a cost and a latency decision, not a
    /// correctness one — the gateway checks the answer is as long as the question either way.
    /// </summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>
    /// What an operator has to have got right before this can start, as a list of problems rather
    /// than a boolean.
    /// <para>
    /// <paramref name="allowedHosts"/> is <c>OutboundAccess:AllowedHosts</c>. A hosted provider
    /// whose host is not on it is refused at startup rather than at the first call: `info.md`
    /// requires that entry as the deliberate statement that this vendor may receive project text,
    /// and a system that discovered the omission on a Tuesday afternoon would have discovered it
    /// by trying to send.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Problems(IEnumerable<string> allowedHosts)
    {
        List<string> problems = [];

        if (Provider == EmbeddingProviderKind.None)
        {
            return problems;
        }

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out Uri? endpoint))
        {
            problems.Add("Embedding:Endpoint must be an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            problems.Add("Embedding:Model must name the model, so the index can record it.");
        }

        if (Dimensions <= 0)
        {
            problems.Add(
                "Embedding:Dimensions must be the vector length the model produces. An index "
                + "built at the wrong dimension answers with nonsense rather than an error.");
        }

        if (BatchSize <= 0)
        {
            problems.Add("Embedding:BatchSize must be at least one.");
        }

        if (Provider != EmbeddingProviderKind.HostedApi)
        {
            return problems;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add("Embedding:ApiKey is required for a hosted provider.");
        }

        if (endpoint is not null
            && !allowedHosts.Any(host =>
                string.Equals(host, endpoint.Host, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add(
                $"A hosted embedding provider sends project text out of this deployment, so "
                + $"'{endpoint.Host}' has to be named in OutboundAccess:AllowedHosts on purpose. "
                + "info.md requires that entry, and an acceptance recorded beside it, before a "
                + "third egress path is opened.");
        }

        return problems;
    }
}
