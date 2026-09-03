using System.Text.RegularExpressions;

namespace DevBuddy.Infrastructure.Scanning;

/// <summary>
/// The rule set the personal-data scanner and redactor share, for the same reason the secret rules
/// are shared between theirs: a redactor that missed what the scanner found would report material
/// as sensitive and release it anyway.
/// <para>
/// Three shapes. A government ID number and a payment card number are recognisable on their own; a
/// birth date or a passport number is not, so those are only caught when explicitly labelled — the
/// same trade the secret scanner makes for a generic <c>password = ...</c> assignment.
/// </para>
/// <para>
/// Accepted limitation, matching AL-2: this catches known shapes. Personal data that does not look
/// like one of them will not be caught here, which is why provenance and correction exist for what
/// detection misses.
/// </para>
/// </summary>
internal static partial class PersonalDataRules
{
    public const string Marker = "[REDACTED]";

    public static IReadOnlyList<PersonalDataRule> All { get; } =
    [
        new("us-ssn", UsSocialSecurityNumber()),
        new("payment-card-number", PaymentCardNumberCandidate(), RequiresLuhn: true),
        new("labelled-personal-data", LabelledPersonalData()),
    ];

    public static bool IsAlreadyRedacted(string value) =>
        value.Contains(Marker, StringComparison.Ordinal);

    /// <summary>
    /// The Luhn checksum. A random 13-to-19-digit run is common in test fixtures, changelogs, and
    /// build identifiers; requiring a valid checksum is what keeps this from firing on all of them
    /// while still catching a real-shaped card number.
    /// </summary>
    public static bool PassesLuhn(string digits)
    {
        int sum = 0;
        bool doubleDigit = false;

        for (int index = digits.Length - 1; index >= 0; index--)
        {
            int digit = digits[index] - '0';

            if (doubleDigit)
            {
                digit *= 2;

                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return digits.Length > 0 && sum % 10 == 0;
    }

    // XXX-XX-XXXX. The grouping is what distinguishes it from a phone number (XXX-XXX-XXXX) and a
    // date (YYYY-MM-DD); both fail this pattern by construction.
    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex UsSocialSecurityNumber();

    // A digit run, 13 to 19 digits long once separators are removed, which is the range every
    // major card scheme's number length falls into. The pattern alone is not enough — see
    // RequiresLuhn on the rule — because a plain thirteen-digit number is common and unremarkable.
    [GeneratedRegex(@"\b\d(?:[ -]?\d){12,18}\b", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex PaymentCardNumberCandidate();

    // A recognised personal-data field, labelled and given a value: "date of birth: 1990-01-01",
    // "passport number = X1234567". The label survives redaction and the value does not, matching
    // how a named secret assignment is handled.
    [GeneratedRegex(
        @"(?i)\b(?:date\s+of\s+birth|dob|passport(?:\s+number)?|driver'?s\s+licen[cs]e(?:\s+number)?|"
        + @"national\s+insurance\s+number|patient\s+id|medical\s+record\s+number)\b\s*[:=]\s*"
        + @"(?<value>""[^""\r\n]{2,}""|'[^'\r\n]{2,}'|[^\s,;)\]}\[]{2,})",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex LabelledPersonalData();
}

/// <summary>
/// One rule: a name for the finding, the pattern that produces a candidate, and whether that
/// candidate needs the Luhn checksum to be accepted rather than merely shaped right.
/// </summary>
internal sealed record PersonalDataRule(string Name, Regex Pattern, bool RequiresLuhn = false)
{
    public bool Accepts(string candidate)
    {
        if (!RequiresLuhn)
        {
            return true;
        }

        string digits = string.Concat(candidate.Where(char.IsAsciiDigit));
        return digits.Length is >= 13 and <= 19 && PersonalDataRules.PassesLuhn(digits);
    }
}
