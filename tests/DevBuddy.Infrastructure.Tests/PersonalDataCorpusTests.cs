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
    ];

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
}
