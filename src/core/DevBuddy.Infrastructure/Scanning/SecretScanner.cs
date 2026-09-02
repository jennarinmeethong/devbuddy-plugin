using System.Text;
using System.Text.RegularExpressions;
using DevBuddy.Application.Abstractions;

namespace DevBuddy.Infrastructure.Scanning;

/// <summary>
/// Finds credentials, connection strings, private keys, and high-entropy material.
/// <para>
/// Control SB-17. Runs before material is retained and again before any response leaves the
/// boundary, and the redactor beside it uses the same rules, so nothing can be reported as
/// sensitive and then released anyway.
/// </para>
/// <para>
/// Findings carry the rule name, the line, and the length. Never the matched text: a finding that
/// quoted the secret it found would put that secret into every log, bug report, and audit entry
/// that touched the result.
/// </para>
/// </summary>
internal sealed class SecretScanner : ISecretScanner
{
    public Task<SecretScanResult> ScanAsync(string content, CancellationToken cancellationToken) =>
        Task.FromResult(Scan(content));

    /// <summary>Synchronous core, so the redactor can share it without an async hop.</summary>
    public static SecretScanResult Scan(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return SecretScanResult.Clean;
        }

        List<SecretFinding> findings = [];

        foreach (SecretRule rule in SecretRules.All)
        {
            foreach (Match match in rule.Pattern.Matches(content))
            {
                string value = ValueOf(match);

                if (SecretRules.IsAlreadyRedacted(value) || !rule.Accepts(value))
                {
                    continue;
                }

                findings.Add(new SecretFinding(rule.Name, LineOf(content, match.Index), match.Length));
            }
        }

        return findings.Count == 0
            ? SecretScanResult.Clean
            : new SecretScanResult([.. findings.OrderBy(finding => finding.LineNumber)]);
    }

    /// <summary>
    /// The part of a match that has to look like a secret. For a named assignment that is the
    /// value, not the name: <c>api_key</c> is a low-entropy word and would drag the average down.
    /// </summary>
    internal static string ValueOf(Match match)
    {
        Group value = match.Groups["value"];
        string text = value.Success ? value.Value : match.Value;
        return text.Trim('"', '\'');
    }

    private static int LineOf(string content, int index)
    {
        int line = 1;

        for (int position = 0; position < index && position < content.Length; position++)
        {
            if (content[position] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}

/// <summary>
/// Replaces what the scanner finds.
/// <para>
/// For a named assignment the name survives and the value goes, so a reader can still see that a
/// configuration file sets an API key without learning which one. For everything else the whole
/// match goes: a partially redacted private key is still a private key.
/// </para>
/// </summary>
internal sealed class SecretRedactor : IRedactor
{
    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        string current = text;

        foreach (SecretRule rule in SecretRules.All)
        {
            current = rule.Pattern.Replace(current, match => Replace(rule, match));
        }

        return current;
    }

    private static string Replace(SecretRule rule, Match match)
    {
        string value = SecretScanner.ValueOf(match);

        // Already redacted, or not actually a secret. Either way it is left exactly as it is,
        // which is what makes a second pass a no-op.
        if (SecretRules.IsAlreadyRedacted(value) || !rule.Accepts(value))
        {
            return match.Value;
        }

        Group valueGroup = match.Groups["value"];

        if (!valueGroup.Success)
        {
            return SecretRules.Marker;
        }

        // Keep everything around the value, so `api_key = "..."` becomes `api_key = [REDACTED]`
        // rather than disappearing entirely.
        var builder = new StringBuilder(match.Value.Length);
        int valueStart = valueGroup.Index - match.Index;

        builder.Append(match.Value.AsSpan(0, valueStart));
        builder.Append(SecretRules.Marker);
        builder.Append(match.Value.AsSpan(valueStart + valueGroup.Length));

        return builder.ToString();
    }
}
