namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Detects credentials, connection strings, private keys, and high-entropy material.
/// <para>
/// Control SB-17 requires this to run before material is retained and again before any response
/// leaves the boundary. Accepted limitation AL-2 applies: detection can miss things, which is
/// why provenance and correction exist rather than a claim of perfect detection.
/// </para>
/// </summary>
public interface ISecretScanner
{
    Task<SecretScanResult> ScanAsync(string content, CancellationToken cancellationToken);
}

/// <summary>What the scanner found. Locations, never the secret values themselves.</summary>
public sealed record SecretScanResult(IReadOnlyList<SecretFinding> Findings)
{
    public bool HasFindings => Findings.Count > 0;

    public static SecretScanResult Clean { get; } = new([]);
}

/// <summary>
/// One finding. Carries the rule that matched and where, and never the matched text: an audit
/// log or a bug report full of real secrets is worse than the problem it describes.
/// </summary>
public sealed record SecretFinding(string RuleName, int LineNumber, int Length);
