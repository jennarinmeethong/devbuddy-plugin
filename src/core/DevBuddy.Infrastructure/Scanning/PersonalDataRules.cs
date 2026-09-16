using System.Globalization;
using System.Text.RegularExpressions;

namespace DevBuddy.Infrastructure.Scanning;

/// <summary>
/// The rule set the personal-data scanner and redactor share, for the same reason the secret rules
/// are shared between theirs: a redactor that missed what the scanner found would report material
/// as sensitive and release it anyway.
/// <para>
/// Two kinds of rule. A government ID number, a payment card number, an email address, and a
/// mobile number are recognisable on their own; a birth date or a passport number is not, so those
/// are only caught when explicitly labelled — in English or in Thai — the same trade the secret
/// scanner makes for a generic <c>password = ...</c> assignment. A bare number is accepted only
/// when its checksum holds, where the scheme has one, because a plain thirteen-digit run is common
/// and unremarkable in engineering text.
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

    // Order matters to the redactor, which applies the rules one after another. An email address
    // goes first so that a local part made of digits is removed with its domain, rather than
    // having a number rule take the digits and leave the domain behind.
    public static IReadOnlyList<PersonalDataRule> All { get; } =
    [
        new("email-address", EmailAddress(), IsNotReservedEmailDomain),
        new("us-ssn", UsSocialSecurityNumber()),
        new("payment-card-number", PaymentCardNumberCandidate(), IsPaymentCardNumber),
        new("thai-national-id", ThaiNationalIdCandidate(), IsThaiNationalId),
        new("thai-mobile-number", ThaiMobileNumber()),
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

    /// <summary>
    /// The Thai national ID check digit: the first twelve digits weighted 13 down to 2, and the
    /// thirteenth equal to <c>(11 - sum mod 11) mod 10</c>. It is what separates a citizen ID from
    /// any other thirteen-digit number, and a random run passes it one time in ten.
    /// </summary>
    public static bool PassesThaiNationalIdChecksum(string digits)
    {
        if (digits.Length != 13)
        {
            return false;
        }

        int sum = 0;

        for (int index = 0; index < 12; index++)
        {
            sum += (digits[index] - '0') * (13 - index);
        }

        return (11 - (sum % 11)) % 10 == digits[12] - '0';
    }

    /// <summary>
    /// The candidate's decimal digits as ASCII. <c>\d</c> in .NET matches every Unicode decimal
    /// digit, Thai digits (๐ to ๙) included, so a checksum that assumed ASCII would reject a
    /// number the pattern had rightly found.
    /// </summary>
    public static string DecimalDigits(string candidate) =>
        string.Concat(candidate
            .Where(char.IsDigit)
            .Select(digit => (char)('0' + CharUnicodeInfo.GetDecimalDigitValue(digit))));

    private static bool IsPaymentCardNumber(string candidate)
    {
        string digits = DecimalDigits(candidate);
        return digits.Length is >= 13 and <= 19 && PassesLuhn(digits);
    }

    private static bool IsThaiNationalId(string candidate) =>
        PassesThaiNationalIdChecksum(DecimalDigits(candidate));

    /// <summary>
    /// Refuses addresses at the domains RFC 2606 reserves for documentation and testing:
    /// <c>example.com</c>, <c>example.net</c>, <c>example.org</c>, and the <c>.example</c>,
    /// <c>.test</c>, <c>.invalid</c> and <c>.localhost</c> top-level domains. No mailbox can exist
    /// there, so an address at one cannot belong to anybody. <c>.local</c> is deliberately not on
    /// this list: an Active Directory domain is often named that way, and its addresses are real
    /// people's.
    /// </summary>
    private static bool IsNotReservedEmailDomain(string candidate)
    {
        string domain = candidate[(candidate.LastIndexOf('@') + 1)..];

        foreach (string reserved in ReservedSecondLevelDomains)
        {
            if (domain.Equals(reserved, StringComparison.OrdinalIgnoreCase)
                || domain.EndsWith("." + reserved, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return !ReservedTopLevelDomains.Contains(domain[(domain.LastIndexOf('.') + 1)..]);
    }

    private static readonly string[] ReservedSecondLevelDomains = ["example.com", "example.net", "example.org"];

    private static readonly HashSet<string> ReservedTopLevelDomains =
        new(["example", "test", "invalid", "localhost"], StringComparer.OrdinalIgnoreCase);

    // An address: a local part, an at sign, and at least one dotted label before an alphabetic
    // top-level domain. Four look-arounds keep it off engineering text that shares the shape:
    // a local part preceded by "/" is URL userinfo (https://user@host); a domain followed by ":"
    // and a path is an scp-style Git remote (git@github.com:org/repo); "logo@2x.png" is a
    // high-density image asset; and a package pin (react@18.3.1) fails by having no alphabetic
    // top-level domain. The boundaries are ASCII on purpose, so an address written straight after
    // Thai text, with no space, is still found.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_.%+/-])[A-Za-z0-9._%+-]{1,64}@(?![1-9]x\.)"
        + @"(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,24}"
        + @"(?![A-Za-z0-9_@-]|\.[A-Za-z0-9]|[:/][^\s])",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex EmailAddress();

    // XXX-XX-XXXX. The grouping is what distinguishes it from a phone number (XXX-XXX-XXXX) and a
    // date (YYYY-MM-DD); both fail this pattern by construction.
    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex UsSocialSecurityNumber();

    // A digit run, 13 to 19 digits long once separators are removed, which is the range every
    // major card scheme's number length falls into. The pattern alone is not enough — see
    // IsPaymentCardNumber — because a plain thirteen-digit number is common and unremarkable.
    [GeneratedRegex(@"\b\d(?:[ -]?\d){12,18}\b", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex PaymentCardNumberCandidate();

    // Thirteen digits in the grouping printed on the card, 1-4-5-2-1, separated by dashes, by
    // spaces, or not at all — one separator throughout, which the backreference enforces. Not part
    // of a longer run on either side. The pattern alone is not enough; see IsThaiNationalId.
    [GeneratedRegex(
        @"(?<![A-Za-z_\d-])\d(?<sep>[- ]?)\d{4}\k<sep>\d{5}\k<sep>\d{2}\k<sep>\d(?![A-Za-z_\d]|-\d)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex ThaiNationalIdCandidate();

    // A Thai mobile number: ten digits beginning 06, 08 or 09, written 081-234-5678, 081 234 5678,
    // 08-1234-5678, 081-2345678 or 0812345678; or the international form, +66 81 234 5678 and
    // +66812345678, including the common mistake of keeping the trunk zero. Fixed lines (02, 03x,
    // 04x, 05x, 07x) are not matched: they are most often a business's number, and their nine
    // digits collide with far more ordinary text. There is no checksum to lean on, so the leading
    // 0[689] and the refusal to match inside a longer digit run carry the whole weight.
    [GeneratedRegex(
        @"(?<![A-Za-z_+.\d-])"
        + @"(?:0[689](?:\d(?<sep>[- ]?)\d{3}\k<sep>\d{4}|-\d{4}-\d{4}|\d-\d{7})"
        + @"|\+66[- ]?(?:\(0\)[- ]?|0)?[689](?:[- ]?\d){8})"
        + @"(?![A-Za-z_\d]|[-.]\d)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex ThaiMobileNumber();

    // A recognised personal-data field, labelled and given a value: "date of birth: 1990-01-01",
    // "passport number = X1234567", "วันเกิด: 12 เมษายน 2533". The label survives redaction and
    // the value does not, matching how a named secret assignment is handled.
    //
    // The Thai labels have no leading word boundary, because Thai is written without spaces
    // between words and a label is usually joined to the word before it. They are specific enough
    // to stand without one. A value written as day, month name and year is taken whole, so neither
    // "12 April 1990" nor "12 เม.ย. 2533" loses only its day.
    [GeneratedRegex(
        @"(?i)(?:\b(?:date\s+of\s+birth|dob|passport(?:\s+number)?|driver'?s\s+licen[cs]e(?:\s+number)?|"
        + @"national\s+insurance\s+number|patient\s+id|medical\s+record\s+number|"
        + @"national\s+id(?:entification)?(?:\s+(?:card\s+)?number)?|citizen\s+id(?:\s+(?:card\s+)?number)?)\b"
        + @"|บัตรประชาชน|ประจำตัวประชาชน|วัน(?:\s*เดือน\s*ปี)?\s*เกิด|หนังสือเดินทาง|พาสปอร์ต"
        + @"|เบอร์(?:โทร(?:ศัพท์)?|มือถือ)|โทรศัพท์(?:มือถือ)?)"
        + @"\s*[:=]\s*"
        + @"(?<value>\d{1,2}\s+\p{L}[\p{L}\p{Mn}.]*\s+\d{2,4}|""[^""\r\n]{2,}""|'[^'\r\n]{2,}'|[^\s,;)\]}\[]{2,})",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex LabelledPersonalData();
}

/// <summary>
/// One rule: a name for the finding, the pattern that produces a candidate, and — where the shape
/// alone is not enough — a check the candidate must pass to be accepted, such as a checksum.
/// </summary>
internal sealed record PersonalDataRule(string Name, Regex Pattern, Func<string, bool>? Check = null)
{
    public bool Accepts(string candidate) => Check is null || Check(candidate);
}
