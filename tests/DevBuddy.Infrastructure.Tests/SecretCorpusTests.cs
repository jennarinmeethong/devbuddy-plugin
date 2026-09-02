using DevBuddy.Application.Abstractions;
using DevBuddy.Infrastructure.Scanning;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The secret corpus. Control SB-17, and the first half of the Phase 6 exit criteria.
/// <para>
/// Every positive case asserts two things: the scanner reports it, and the redactor removes it.
/// Testing only detection would allow a rule that reports a secret and then hands it over anyway,
/// which is the worst of both.
/// </para>
/// </summary>
public sealed class SecretCorpusTests
{
    private static readonly SecretScanner Scanner = new();
    private static readonly SecretRedactor Redactor = new();

    // Assemble this synthetic fixture at runtime so GitHub push protection does not flag the source.
    private static readonly string SlackToken = string.Join("-", "xoxb", "123456789012", "abcdefghijklmnop");

    public static TheoryData<string, string, string> Positives() => new()
    {
        {
            "aws-access-key-id",
            "AKIAIOSFODNN7EXAMPLE",
            "aws_access_key = AKIAIOSFODNN7EXAMPLE"
        },
        {
            "github-token",
            "ghp_1234567890abcdefghijklmnopqrstuvwxyzAB",
            "GITHUB_TOKEN=ghp_1234567890abcdefghijklmnopqrstuvwxyzAB"
        },
        {
            "slack-token",
            SlackToken,
            $"slack hook uses {SlackToken}"
        },
        {
            "json-web-token",
            "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dBjftJeZ4CVPmB92K27uhbUJU1p1r_wW1gFWFOEjXk",
            "Authorization header carried eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dBjftJeZ4CVPmB92K27uhbUJU1p1r_wW1gFWFOEjXk"
        },
        {
            "assigned-secret",
            "hunter2-and-then-some",
            "password: hunter2-and-then-some"
        },
        {
            "assigned-secret",
            "sk-abcdefghijklmnopqrstuvwx",
            "api_key = \"sk-abcdefghijklmnopqrstuvwx\""
        },
        {
            "connection-string",
            "Password=s3cr3t-p4ssw0rd",
            "Server=db.internal;Database=devbuddy;User Id=app;Password=s3cr3t-p4ssw0rd;"
        },
        {
            "high-entropy-string",
            "Zm9vYmFyYmF6cXV4MTIzNDU2Nzg5MEFCQ0RFRkdISUpLTE0",
            "the blob was Zm9vYmFyYmF6cXV4MTIzNDU2Nzg5MEFCQ0RFRkdISUpLTE0"
        },
    };

    /// <summary>
    /// Text that must survive untouched. A redactor that removes ordinary prose, file paths, or
    /// identifiers makes the system unusable, and people route around unusable controls.
    /// </summary>
    public static TheoryData<string> Negatives() =>
    [
        "The importer normalises identifiers before the validator sees them.",
        "See src/core/DevBuddy.Application/UseCases/Lifecycle/LifecycleUseCases.cs for the detail.",
        "Server=db.internal;Database=devbuddy;Integrated Security=true;",
        "password rotation is documented in the runbook",
        "commit e10fc43 introduced the persistence layer",
        "AKIA is the prefix AWS uses for access key identifiers",
    ];

    [Theory]
    [MemberData(nameof(Positives))]
    public async Task the_scanner_finds_it_and_the_redactor_removes_it(
        string expectedRule, string secret, string content)
    {
        SecretScanResult result = await Scanner.ScanAsync(content, CancellationToken.None);

        Assert.True(result.HasFindings, $"Expected {expectedRule} in: {content}");
        Assert.Contains(result.Findings, finding => finding.RuleName == expectedRule);

        // The finding names the rule and the line. It never carries the value.
        Assert.All(result.Findings, finding =>
            Assert.DoesNotContain(secret, finding.RuleName, StringComparison.Ordinal));

        string redacted = Redactor.Redact(content);

        Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Negatives))]
    public async Task ordinary_text_is_neither_flagged_nor_altered(string content)
    {
        SecretScanResult result = await Scanner.ScanAsync(content, CancellationToken.None);

        Assert.False(result.HasFindings, $"Unexpected finding in: {content}");
        Assert.Equal(content, Redactor.Redact(content));
    }

    [Fact]
    public async Task a_private_key_block_is_removed_whole()
    {
        string content = string.Join(
            Environment.NewLine,
            "-----BEGIN RSA PRIVATE KEY-----",
            "MIIEowIBAAKCAQEAwXyz1234567890abcdefghijklmnopqrstuvwxyzABCDEFGH",
            "IJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHI",
            "-----END RSA PRIVATE KEY-----");

        SecretScanResult result = await Scanner.ScanAsync(content, CancellationToken.None);
        Assert.Contains(result.Findings, finding => finding.RuleName == "private-key-block");

        // Whole, not just the header. A key body with its markers stripped is still a key.
        string redacted = Redactor.Redact(content);
        Assert.DoesNotContain("MIIEowIBAAKCAQEA", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN RSA PRIVATE KEY", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_named_assignment_keeps_the_name_and_loses_the_value()
    {
        const string Content = "api_key = \"sk-abcdefghijklmnopqrstuvwx\"";

        string redacted = Redactor.Redact(Content);

        // A reader can still see that this file sets an API key, without learning which one.
        Assert.Contains("api_key", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwx", redacted, StringComparison.Ordinal);

        Assert.True((await Scanner.ScanAsync(Content, CancellationToken.None)).HasFindings);
    }

    [Fact]
    public async Task findings_carry_the_line_a_reader_has_to_go_and_fix()
    {
        string content = string.Join(
            "\n", "first line is fine", "still fine", "password: hunter2-and-then-some");

        SecretScanResult result = await Scanner.ScanAsync(content, CancellationToken.None);

        Assert.Equal(3, Assert.Single(result.Findings).LineNumber);
    }

    [Fact]
    public async Task several_secrets_in_one_document_are_all_found_and_all_removed()
    {
        string content = string.Join(
            "\n",
            "AWS_KEY=AKIAIOSFODNN7EXAMPLE",
            "GITHUB=ghp_1234567890abcdefghijklmnopqrstuvwxyzAB",
            "password: hunter2-and-then-some");

        SecretScanResult result = await Scanner.ScanAsync(content, CancellationToken.None);
        Assert.True(result.Findings.Count >= 3);

        string redacted = Redactor.Redact(content);
        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_1234567890", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void redacting_twice_changes_nothing_the_second_time()
    {
        const string Content = "password: hunter2-and-then-some and AKIAIOSFODNN7EXAMPLE";

        string once = Redactor.Redact(Content);
        Assert.Equal(once, Redactor.Redact(once));
    }

    [Fact]
    public void entropy_separates_a_key_from_a_sentence()
    {
        // The threshold is what keeps the long-string rule from redacting every identifier in the
        // codebase. Both sides of it are asserted so a change to it cannot pass unnoticed.
        Assert.True(SecretRules.EntropyOf("Zm9vYmFyYmF6cXV4MTIzNDU2Nzg5MEFCQ0RFRkdI") >= 4.0);
        Assert.True(SecretRules.EntropyOf("the importer normalises identifiers") < 4.0);
    }

    [Fact]
    public async Task empty_and_null_content_are_handled_rather_than_thrown_at()
    {
        Assert.False((await Scanner.ScanAsync(string.Empty, CancellationToken.None)).HasFindings);
        Assert.Equal(string.Empty, Redactor.Redact(string.Empty));
        Assert.Equal(string.Empty, Redactor.Redact(null!));
    }
}
