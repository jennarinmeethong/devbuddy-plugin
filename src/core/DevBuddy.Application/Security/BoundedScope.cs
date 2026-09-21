using System.Text.Json;

namespace DevBuddy.Application.Security;

/// <summary>
/// The personal-data rules SB-18 applies, by name. The names are the contract between an approved
/// bounded scope and the scanner that enforces it, so they are listed here, where a request can be
/// validated against them, and a test holds the scanner's rule set to exactly this list.
/// </summary>
public static class PersonalDataRuleNames
{
    public static IReadOnlyList<string> All { get; } =
    [
        "email-address",
        "us-ssn",
        "payment-card-number",
        "thai-national-id",
        "thai-mobile-number",
        "labelled-personal-data",
    ];
}

/// <summary>
/// An approved bounded data scope for one project's AI access (SB-18, Phase 13 B9): which
/// personal-data rules the AI channel may see through, and why the owner approved it.
/// <para>
/// Every rule not named stays in force: blocked from a draft, redacted from a read. A secret is
/// refused inside any scope, because secrets are a different control (SB-17) that no scope reaches.
/// </para>
/// <para>
/// Before Phase 13 a scope was free text, and any text at all switched SB-18 off for the project.
/// A stored value that is not this structure is still read that way, as <see cref="Unstructured"/>,
/// so an installation that approved one keeps what it approved. A new approval is always
/// structured.
/// </para>
/// </summary>
public sealed record BoundedScope(IReadOnlyList<string> AllowedPersonalDataRules, string? Justification, bool Unstructured)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool Allows(string ruleName) =>
        Unstructured || AllowedPersonalDataRules.Contains(ruleName, StringComparer.Ordinal);

    public static BoundedScope? Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        if (stored.TrimStart().StartsWith('{'))
        {
            try
            {
                if (JsonSerializer.Deserialize<Stored>(stored, Json) is { AllowedPersonalDataRules: { } rules } parsed)
                {
                    return new BoundedScope([.. rules], parsed.Justification, Unstructured: false);
                }
            }
            catch (JsonException)
            {
                // Falls through to the reading the old free-text form always had.
            }
        }

        return new BoundedScope([], stored, Unstructured: true);
    }

    public static string Compose(IEnumerable<string> allowedRules, string justification) =>
        JsonSerializer.Serialize(
            new Stored([.. allowedRules.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], justification),
            Json);

    private sealed record Stored(List<string>? AllowedPersonalDataRules, string? Justification);
}
