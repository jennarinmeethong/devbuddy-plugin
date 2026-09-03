namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Detects customer data, production data, and personal information: government ID numbers,
/// payment card numbers, and explicitly labelled personal fields.
/// <para>
/// Control SB-18. Separate from <see cref="ISecretScanner"/> because the two controls have
/// different defaults: a secret is denied everywhere, with no exception. Personal data is denied
/// only on the AI channel, and only until a project owner separately approves a bounded scope for
/// it — the pipeline is what applies that exception, not this scanner, which only ever reports
/// what it finds.
/// </para>
/// <para>
/// The same accepted limitation as the secret scanner applies: this catches known shapes. It will
/// miss personal data that does not look like one of them, and provenance and correction are what
/// the system depends on for what it misses.
/// </para>
/// </summary>
public interface IPersonalDataScanner
{
    Task<PersonalDataScanResult> ScanAsync(string content, CancellationToken cancellationToken);
}

/// <summary>What the scanner found. Locations, never the matched values themselves.</summary>
public sealed record PersonalDataScanResult(IReadOnlyList<PersonalDataFinding> Findings)
{
    public bool HasFindings => Findings.Count > 0;

    public static PersonalDataScanResult Clean { get; } = new([]);
}

public sealed record PersonalDataFinding(string RuleName, int LineNumber, int Length);

/// <summary>
/// Replaces personal data with a marker, the way <see cref="IRedactor"/> does for secrets.
/// <para>
/// A separate interface rather than a second implementation of <see cref="IRedactor"/>, because
/// the two are applied under different conditions: secret redaction runs unconditionally, and
/// personal-data redaction runs only when the caller is on the AI channel and no bounded scope has
/// been approved for the project. Keeping them as distinct ports is what lets the pipeline compose
/// them explicitly rather than one redactor silently deciding when to apply the other's rules.
/// </para>
/// </summary>
public interface IPersonalDataRedactor
{
    string Redact(string text);
}
