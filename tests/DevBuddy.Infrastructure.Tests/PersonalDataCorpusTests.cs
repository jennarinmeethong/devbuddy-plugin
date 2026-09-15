using DevBuddy.Application.Abstractions;
using DevBuddy.Infrastructure.Scanning;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The personal-data corpus. Control SB-18, the detection half.
/// <para>
/// Every positive case asserts two things: the scanner reports it, and the redactor removes it —
/// the same discipline <c>SecretCorpusTests</c> holds the secret rules to, and for the same
/// reason: a rule that reports personal data and then hands it over anyway is worse than no rule.
/// </para>
/// <para>
/// What the pipeline does with a finding — block a draft, redact a read, or let it through because
/// a bounded scope was approved — is a different concern, tested where that decision is made
/// (<c>PersonalDataPolicyTests</c> and the pipeline gating tests). This file is only about whether
/// the shapes are recognised at all.
/// </para>
/// </summary>
public sealed class PersonalDataCorpusTests
{
    private static readonly PersonalDataScanner Scanner = new();
    private static readonly PersonalDataRedactor Redactor = new();

    // Widely published test numbers, the same way AKIAIOSFODNN7EXAMPLE is a published example AWS
    // key rather than anybody's real one. Luhn-valid, so they exercise the checksum honestly.
    private const string VisaTestNumber = "4111111111111111";
    private const string MastercardTestNumber = "5500000000000004";

    // Invented Thai national ID numbers that pass the mod-11 check digit, and fail Luhn, so each
    // is reported by the Thai rule alone rather than by the card rule as well.
    private const string ThaiNationalId = "1-1037-01234-56-3";
    private const string OtherThaiNationalId = "3100500123458";

    // The number observed on 2026-09-15 getting past the scanner. Invented, and its check digit is
    // wrong: the checksum wants 3, not 7. Kept because it is what a person writing an example
    // actually types.
    private const string ObservedInventedNationalId = "1-1037-01234-56-7";

    // An address at a domain nobody has registered for this purpose, because an address at a
    // reserved documentation domain is deliberately not reported — see
    // an_address_at_a_reserved_documentation_domain_is_not_flagged.
    private const string EmailAddress = "somchai.testperson@devbuddy-fixture.co.th";

    public static TheoryData<string, string, string> Positives() => new()
    {
        {
            "us-ssn",
            "123-45-6789",
            "on file under SSN 123-45-6789"
        },
        {
            "payment-card-number",
            VisaTestNumber,
            $"card on file: {VisaTestNumber}"
        },
        {
            "payment-card-number",
            MastercardTestNumber,
            $"charged to {MastercardTestNumber} on renewal"
        },
        {
            "payment-card-number",
            "4111-1111-1111-1111",
            "card on file: 4111-1111-1111-1111"
        },
        {
            "labelled-personal-data",
            "1990-04-12",
            "date of birth: 1990-04-12"
        },
        {
            "labelled-personal-data",
            "X1234567",
            "passport number = X1234567"
        },
        {
            "labelled-personal-data",
            "12 April 1990",
            "date of birth: 12 April 1990"
        },

        // Thai national ID: dashed, bare, spaced, joined to Thai text, and in Thai digits.
        {
            "thai-national-id",
            ThaiNationalId,
            $"ผู้ร้องเรียน {ThaiNationalId} ติดต่อกลับแล้ว"
        },
        {
            "thai-national-id",
            OtherThaiNationalId,
            $"customer record {OtherThaiNationalId} was merged"
        },
        {
            "thai-national-id",
            "3 1005 00123 45 8",
            "ID 3 1005 00123 45 8 on the scanned form"
        },
        {
            "thai-national-id",
            OtherThaiNationalId,
            $"เลขที่{OtherThaiNationalId}ครับ"
        },
        {
            "thai-national-id",
            "๑-๑๐๓๗-๐๑๒๓๔-๕๖-๓",
            "สำเนาบัตร ๑-๑๐๓๗-๐๑๒๓๔-๕๖-๓"
        },

        // Thai mobile numbers, in every grouping people write them in.
        {
            "thai-mobile-number",
            "081-234-5678",
            "โทรกลับที่ 081-234-5678 หลังบ่ายสอง"
        },
        {
            "thai-mobile-number",
            "0812345678",
            "customer callback 0812345678 after 2pm"
        },
        {
            "thai-mobile-number",
            "091 234 5678",
            "on-call mobile 091 234 5678"
        },
        {
            "thai-mobile-number",
            "08-1234-5678",
            "reach them on 08-1234-5678"
        },
        {
            "thai-mobile-number",
            "061-2345678",
            "reach them on 061-2345678"
        },
        {
            "thai-mobile-number",
            "+66 81 234 5678",
            "international: +66 81 234 5678"
        },
        {
            "thai-mobile-number",
            "+66812345678",
            "SMS sent to +66812345678 at 09:00"
        },
        {
            "thai-mobile-number",
            "+66 081 234 5678",
            "SMS sent to +66 081 234 5678 at 09:00"
        },

        // Email addresses, including one joined straight onto Thai text.
        {
            "email-address",
            EmailAddress,
            $"escalated by {EmailAddress} on Monday"
        },
        {
            "email-address",
            "somchai@devbuddy-fixture.co.th",
            "ติดต่อsomchai@devbuddy-fixture.co.thครับ"
        },
        {
            "email-address",
            "j.smith@corp.local",
            "reported by mailto:j.smith@corp.local"
        },

        // Thai labels, and the English ones Thai engineering text uses for the same fields.
        {
            "labelled-personal-data",
            ObservedInventedNationalId,
            $"เลขบัตรประชาชน: {ObservedInventedNationalId}"
        },
        {
            "labelled-personal-data",
            ObservedInventedNationalId,
            $"citizen id: {ObservedInventedNationalId}"
        },
        {
            "labelled-personal-data",
            ObservedInventedNationalId,
            $"national id number = {ObservedInventedNationalId}"
        },
        {
            "labelled-personal-data",
            "12 เมษายน 2533",
            "ลูกค้าวันเกิด: 12 เมษายน 2533"
        },
        {
            "labelled-personal-data",
            "12 เม.ย. 2533",
            "วัน เดือน ปีเกิด = 12 เม.ย. 2533"
        },
        {
            "labelled-personal-data",
            "AA1234567",
            "เลขที่หนังสือเดินทาง: AA1234567"
        },
        {
            "labelled-personal-data",
            "02-123-4567",
            "เบอร์โทรศัพท์: 02-123-4567"
        },
    };

    /// <summary>
    /// Text that must survive untouched. A scanner that flags every date, every long number, or
    /// every mention of an ID field makes the system unusable, and the whole point of a project
    /// owner approving a bounded scope is that ordinary engineering knowledge should not need one.
    /// </summary>
    public static TheoryData<string> Negatives() =>
    [
        "The migration ran on 2026-09-01 without incident.",
        "Build 1234567890123456 failed on the arm64 runner.",
        "Ticket reference 9999999999999999 is still open.",
        "Call the support line and quote order 555-2024-01.",
        "The importer assigns a customer id, not a passport number, to new records.",
        "See docs/adr/0009-retention.md for the schedule.",
        "date of birth fields are validated but never logged in full",

        // Thirteen digits with the wrong check digit, in Thai text and on its own.
        "เลขที่คำสั่งซื้อ 1103701234567 ถูกยกเลิก",
        "Batch 1757894400000 was replayed.",

        // Shapes an email pattern would take by mistake in engineering text.
        "Push to git@github.com:jennarinmeethong/devbuddy.git from the release runner.",
        "Clone https://ci-bot@github.com/org/repo.git with the deploy key.",
        "Ship logo@2x.png and icon@3x.png in the bundle.",
        "Pin react@18.3.1 and @types/node@20.11.5 in package.json.",
        "Contact the owners listed in CODEOWNERS (@devbuddy/security).",
        "Seed data uses jane.doe@example.com, qa@staging.test and ops@example.org.",

        // Shapes a phone pattern would take by mistake.
        "Trace 08123456-7890-4abc-9def-0123456789ab failed at 08:12:34.",
        "+66 is Thailand's country calling code.",
        "Version 0.81.2345678 was yanked.",
        "The Unix timestamp 1757894400 marks the cut-over.",

        // Thai labels without a value.
        "ช่องวันเกิดต้องกรอก และเบอร์โทรไม่บังคับ",
        "The วันเกิด field is required on the form.",
    ];

    /// <summary>Every piece of text in the corpus, positive and negative, for the parity test.</summary>
    public static TheoryData<string> Everything()
    {
        TheoryData<string> all = [];

        foreach (object[] row in Positives())
        {
            all.Add((string)row[2]);
        }

        foreach (object[] row in Negatives())
        {
            all.Add((string)row[0]);
        }

        return all;
    }

    [Theory]
    [MemberData(nameof(Positives))]
    public async Task the_scanner_finds_it_and_the_redactor_removes_it(
        string expectedRule, string value, string content)
    {
        PersonalDataScanResult result = await Scanner.ScanAsync(content, CancellationToken.None);

        Assert.True(result.HasFindings, $"Expected {expectedRule} in: {content}");
        Assert.Contains(result.Findings, finding => finding.RuleName == expectedRule);

        // The finding names the rule and the line. It never carries the value.
        Assert.All(result.Findings, finding =>
            Assert.DoesNotContain(value, finding.RuleName, StringComparison.Ordinal));

        string redacted = Redactor.Redact(content);

        Assert.DoesNotContain(value, redacted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Negatives))]
    public async Task ordinary_text_is_neither_flagged_nor_altered(string content)
    {
        PersonalDataScanResult result = await Scanner.ScanAsync(content, CancellationToken.None);

        Assert.False(result.HasFindings, $"Unexpected finding in: {content}");
        Assert.Equal(content, Redactor.Redact(content));
    }

    /// <summary>
    /// The scanner and the redactor share one rule set, and this is what holding them to it looks
    /// like: text is altered exactly when something was found, and nothing the scanner can still
    /// find survives redaction. A rule order in the redactor that took half of a match and left
    /// the other half — digits out of an address, leaving its domain — fails the second assertion.
    /// </summary>
    [Theory]
    [MemberData(nameof(Everything))]
    public async Task the_redactor_alters_exactly_what_the_scanner_reports(string content)
    {
        PersonalDataScanResult found = await Scanner.ScanAsync(content, CancellationToken.None);
        string redacted = Redactor.Redact(content);

        Assert.Equal(found.HasFindings, redacted != content);

        PersonalDataScanResult left = await Scanner.ScanAsync(redacted, CancellationToken.None);
        Assert.False(left.HasFindings, $"Still found after redaction: {redacted}");
    }

    [Fact]
    public async Task a_labelled_field_keeps_its_label_and_loses_its_value()
    {
        const string Content = "passport number = X1234567";

        string redacted = Redactor.Redact(Content);

        // A reader can still see that this record names a passport, without learning which one.
        Assert.Contains("passport number", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("X1234567", redacted, StringComparison.Ordinal);

        Assert.True((await Scanner.ScanAsync(Content, CancellationToken.None)).HasFindings);
    }

    [Fact]
    public async Task a_thai_label_keeps_its_label_and_loses_its_value()
    {
        const string Content = "วันเกิด: 12 เมษายน 2533";

        string redacted = Redactor.Redact(Content);

        Assert.Equal("วันเกิด: [REDACTED]", redacted);
        Assert.True((await Scanner.ScanAsync(Content, CancellationToken.None)).HasFindings);
    }

    [Fact]
    public async Task a_card_number_that_fails_the_checksum_is_not_flagged()
    {
        // Sixteen digits, the right shape, and not a real card number by construction: it fails
        // Luhn. Without the checksum this would be indistinguishable from the positive cases
        // above, and the rule would fire on any sixteen-digit build number or ticket reference.
        const string Content = "reference number 1234567890123456 was logged in error";

        PersonalDataScanResult result = await Scanner.ScanAsync(Content, CancellationToken.None);

        Assert.False(result.HasFindings);
        Assert.Equal(Content, Redactor.Redact(Content));
    }

    [Theory]
    [InlineData("1103701234563", true)]
    [InlineData("3100500123458", true)]
    [InlineData("1757894400001", true)]
    [InlineData("1103701234567", false)]
    [InlineData("3100500123450", false)]
    [InlineData("0000000000000", false)]
    [InlineData("110370123456", false)]
    [InlineData("11037012345630", false)]
    public void the_thai_national_id_checksum_is_the_mod_11_check_digit(string digits, bool passes) =>
        Assert.Equal(passes, PersonalDataRules.PassesThaiNationalIdChecksum(digits));

    [Fact]
    public async Task the_observed_invented_id_is_caught_only_when_labelled()
    {
        // The input from 2026-09-15. Its check digit is wrong, so the bare number is not reported,
        // exactly as a sixteen-digit number failing Luhn is not. Written after a label, it is.
        string bare = $"ID {ObservedInventedNationalId} on the form";
        string labelled = $"เลขบัตรประชาชน: {ObservedInventedNationalId}";

        Assert.False((await Scanner.ScanAsync(bare, CancellationToken.None)).HasFindings);
        Assert.Equal(bare, Redactor.Redact(bare));

        PersonalDataScanResult result = await Scanner.ScanAsync(labelled, CancellationToken.None);
        Assert.Equal("labelled-personal-data", Assert.Single(result.Findings).RuleName);
    }

    [Fact]
    public async Task a_millisecond_timestamp_can_pass_the_thai_national_id_checksum()
    {
        // Recorded as a known cost rather than hidden. 1757894400001 is 2025-09-15T00:00:00.001Z
        // in milliseconds, and it passes the check digit, as one bare thirteen-digit run in ten
        // will. The card rule makes the same trade with Luhn.
        const string Content = "event at 1757894400001 was replayed";

        PersonalDataScanResult result = await Scanner.ScanAsync(Content, CancellationToken.None);

        Assert.Equal("thai-national-id", Assert.Single(result.Findings).RuleName);
    }

    [Fact]
    public async Task an_address_at_a_reserved_documentation_domain_is_not_flagged()
    {
        // The address observed on 2026-09-15. example.com is reserved by RFC 2606, so no mailbox
        // can exist there and the address belongs to nobody. The same local part at a domain that
        // could be real is reported.
        const string Reserved = "escalated by somchai.testperson@example.com";
        string real = $"escalated by {EmailAddress}";

        Assert.False((await Scanner.ScanAsync(Reserved, CancellationToken.None)).HasFindings);
        Assert.Equal(Reserved, Redactor.Redact(Reserved));

        Assert.True((await Scanner.ScanAsync(real, CancellationToken.None)).HasFindings);
    }

    [Fact]
    public async Task a_fixed_line_number_is_caught_only_when_labelled()
    {
        // A known limit, not an oversight: a Bangkok landline is most often a business's number,
        // and nine digits collide with far more ordinary text than a mobile number does.
        const string Bare = "reach the office on 02-123-4567";

        Assert.False((await Scanner.ScanAsync(Bare, CancellationToken.None)).HasFindings);
        Assert.True((await Scanner.ScanAsync("เบอร์โทร: 02-123-4567", CancellationToken.None)).HasFindings);
    }

    [Fact]
    public async Task redaction_is_idempotent()
    {
        const string Content = "on file under SSN 123-45-6789";

        string once = Redactor.Redact(Content);
        string twice = Redactor.Redact(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public async Task several_findings_in_one_document_are_all_found_and_all_removed()
    {
        string content = string.Join(
            "\n",
            $"SSN: 123-45-6789",
            $"card on file: {VisaTestNumber}",
            "date of birth: 1990-04-12");

        PersonalDataScanResult result = await Scanner.ScanAsync(content, CancellationToken.None);
        Assert.Equal(3, result.Findings.Count);

        string redacted = Redactor.Redact(content);
        Assert.DoesNotContain("123-45-6789", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(VisaTestNumber, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("1990-04-12", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_thai_customer_note_is_found_rule_by_rule_and_removed_whole()
    {
        string content = string.Join(
            "\n",
            $"เลขบัตรประชาชน {ThaiNationalId}",
            "โทร 081-234-5678",
            $"อีเมล {EmailAddress}",
            "วันเกิด: 12 เมษายน 2533");

        PersonalDataScanResult result = await Scanner.ScanAsync(content, CancellationToken.None);

        Assert.Equal(
            new[] { "thai-national-id", "thai-mobile-number", "email-address", "labelled-personal-data" },
            result.Findings.Select(finding => finding.RuleName));
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Findings.Select(finding => finding.LineNumber));

        string redacted = Redactor.Redact(content);
        Assert.DoesNotContain(ThaiNationalId, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("081-234-5678", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(EmailAddress, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("2533", redacted, StringComparison.Ordinal);
    }
}
