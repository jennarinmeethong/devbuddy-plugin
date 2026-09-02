using System.Text.RegularExpressions;

namespace DevBuddy.Infrastructure.Scanning;

/// <summary>
/// The rule set the scanner and the redactor share.
/// <para>
/// One list, used by both, because a redactor that removed less than the scanner found would be a
/// silent hole: material would be reported as sensitive and released anyway. Every rule here both
/// detects and redacts.
/// </para>
/// <para>
/// Accepted limitation AL-2 applies and is worth stating plainly: this catches known shapes and
/// high-entropy strings. It will miss a secret that looks like prose, and it will occasionally
/// flag a hash that is not one. It is a control, not a guarantee, and the system depends on
/// provenance and correction for what it misses.
/// </para>
/// </summary>
internal static partial class SecretRules
{
    /// <summary>What a redacted match is replaced with. Short, and obvious to a human reader.</summary>
    public const string Marker = "[REDACTED]";

    /// <summary>
    /// Ordered most specific first. A private key block should be reported as a private key, not
    /// as a high-entropy string.
    /// </summary>
    public static IReadOnlyList<SecretRule> All { get; } =
    [
        new("private-key-block", PrivateKeyBlock()),
        new("connection-string", ConnectionString()),
        new("aws-access-key-id", AwsAccessKeyId()),
        new("github-token", GitHubToken()),
        new("slack-token", SlackToken()),
        new("json-web-token", JsonWebToken()),
        new("bearer-token", BearerToken()),
        new("assigned-secret", AssignedSecret()),
        new("high-entropy-string", HighEntropyCandidate(), RequiresEntropy: true),
    ];

    /// <summary>
    /// True when a value has already been through the redactor.
    /// <para>
    /// Redaction has to be idempotent, and the marker itself trips the generic assignment rule:
    /// <c>password: [REDACTED]</c> looks exactly like <c>password: something</c>. Without this,
    /// a second pass would redact the marker and leave a stray bracket behind, and a scan of
    /// already-redacted text would report secrets that are no longer there.
    /// </para>
    /// </summary>
    public static bool IsAlreadyRedacted(string value) =>
        value.Contains(Marker, StringComparison.Ordinal);

    /// <summary>
    /// Shannon entropy per character. A base64 secret sits near 5; English prose and identifiers
    /// sit well below 4. The threshold is deliberately conservative: a false negative here is a
    /// missed secret, but a false positive redacts something a reader needed.
    /// </summary>
    public static double EntropyOf(string value)
    {
        if (value.Length == 0)
        {
            return 0;
        }

        var counts = new Dictionary<char, int>();

        foreach (char character in value)
        {
            counts[character] = counts.TryGetValue(character, out int existing) ? existing + 1 : 1;
        }

        double entropy = 0;

        foreach (int count in counts.Values)
        {
            double probability = (double)count / value.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    // A PEM block. Matched whole, so the redacted output does not leave the body of the key
    // behind with only its header removed.
    [GeneratedRegex(
        @"-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----[\s\S]*?-----END (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex PrivateKeyBlock();

    // A connection string carrying a password. The host and database are not the secret; the
    // credential is, and the whole value goes because splitting it reliably is not worth the risk.
    [GeneratedRegex(
        @"(?i)\b(?:server|host|data\s+source)\s*=\s*[^;\s]+;[^\n\r]*?\b(?:password|pwd)\s*=\s*[^;\s""']+",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex ConnectionString();

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex AwsAccessKeyId();

    [GeneratedRegex(@"\bgh[pousr]_[A-Za-z0-9]{36,255}\b", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\bxox[abprs]-[A-Za-z0-9-]{10,}\b", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex SlackToken();

    [GeneratedRegex(
        @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex JsonWebToken();

    [GeneratedRegex(
        @"(?i)\bauthorization\s*:\s*(?:bearer|basic)\s+[A-Za-z0-9._\-+/=]{16,}",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex BearerToken();

    // The generic case: something named like a credential, assigned a value. Catches
    // password=..., api_key: ..., SECRET_TOKEN="...". The name is kept and the value goes.
    //
    // The unquoted form excludes a leading bracket, which is what keeps redaction idempotent:
    // without it, `password: [REDACTED]` matches with a captured value of `[REDACTED`, and a
    // second pass redacts the marker and leaves the closing bracket behind.
    [GeneratedRegex(
        @"(?i)\b(?:password|passwd|pwd|secret|api[_-]?key|apikey|access[_-]?token|auth[_-]?token|private[_-]?key|client[_-]?secret)\b\s*[:=]\s*(?<value>""[^""\r\n]{4,}""|'[^'\r\n]{4,}'|[^\s,;)\]}\[]{4,})",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex AssignedSecret();

    // Long unbroken base64-ish or hex runs. Only reported when the entropy check agrees, so
    // ordinary identifiers and file hashes in prose do not trip it constantly.
    [GeneratedRegex(@"\b[A-Za-z0-9+/_-]{32,}={0,2}\b", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex HighEntropyCandidate();
}

/// <summary>
/// One rule: a name for the finding and the pattern that produces it.
/// <para>
/// <paramref name="RequiresEntropy"/> marks the heuristic rules, where a pattern match alone is
/// not enough. Without it the long-string rule would redact every commit hash and identifier in
/// the codebase.
/// </para>
/// </summary>
internal sealed record SecretRule(string Name, Regex Pattern, bool RequiresEntropy = false)
{
    /// <summary>
    /// Entropy at or above this is treated as a secret. Base64 keys sit near 5.0; hex hashes and
    /// long identifiers sit near or below 3.8.
    /// </summary>
    public const double EntropyThreshold = 4.0;

    public bool Accepts(string candidate) =>
        !RequiresEntropy || SecretRules.EntropyOf(candidate) >= EntropyThreshold;
}
