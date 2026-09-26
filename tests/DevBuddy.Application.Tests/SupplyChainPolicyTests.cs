using System.Text.RegularExpressions;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The rules the supply-chain scans rest on, approved 2026-09-26 (<c>info.md</c>).
/// <para>
/// A scanner is only as good as what it is allowed to skip, and what runs it. So: every action a
/// workflow uses is pinned by commit, because a tag can be moved by whoever controls it; every
/// scanner image is pinned by digest for the same reason; a Gitleaks finding is accepted by its
/// exact fingerprint and never by a pattern; and a Trivy finding is accepted with a statement of
/// why and a date it stops being accepted. Each of these is a line somebody could relax to make a
/// build pass, which is why they are checked here rather than trusted.
/// </para>
/// </summary>
public sealed partial class SupplyChainPolicyTests
{
    [Fact]
    public void every_action_is_pinned_by_commit_with_its_version_beside_it()
    {
        string[] unpinned = Workflows()
            .SelectMany(workflow => File.ReadAllLines(workflow.FullName)
                .Select((line, index) => (workflow.Name, Line: index + 1, Text: line.Trim())))
            .Where(line => line.Text.StartsWith("uses:", StringComparison.Ordinal)
                || line.Text.StartsWith("- uses:", StringComparison.Ordinal))
            .Where(line => !PinnedAction().IsMatch(line.Text))
            .Select(line => $"{line.Name}:{line.Line}: {line.Text}")
            .ToArray();

        Assert.True(
            unpinned.Length == 0,
            "Every `uses:` must name a 40-character commit and carry its version in a comment, "
            + "as `owner/action@<sha> # vX.Y.Z`, so Dependabot can move both:\n  "
            + string.Join("\n  ", unpinned));
    }

    [Fact]
    public void every_scanner_image_a_workflow_runs_is_pinned_by_digest()
    {
        string[] images = Workflows()
            .SelectMany(workflow => File.ReadAllLines(workflow.FullName))
            .Select(line => line.Trim())
            .Where(line => ImageVariable().IsMatch(line))
            .ToArray();

        Assert.NotEmpty(images);
        Assert.All(images, line => Assert.Matches(DigestPinned(), line));
    }

    [Fact]
    public void a_gitleaks_finding_is_accepted_by_its_exact_fingerprint_only()
    {
        string[] entries = File.ReadAllLines(RepositoryLayout.ProjectFile(".gitleaksignore"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        Assert.All(entries, entry => Assert.Matches(GitleaksFingerprint(), entry));
    }

    [Fact]
    public void every_accepted_trivy_finding_says_why_and_expires()
    {
        string text = File.ReadAllText(RepositoryLayout.ProjectFile(".trivyignore.yaml"));

        // One chunk per entry. The file is small and flat on purpose; a YAML parser would accept
        // shapes Trivy reads differently, and this test is about the shape Trivy reads.
        string[] entries = EntryStart().Split(text).Skip(1).ToArray();

        Assert.NotEmpty(entries);

        foreach (string entry in entries)
        {
            string id = entry.Split('\n')[0].Trim();

            Assert.True(
                entry.Contains("statement:", StringComparison.Ordinal),
                $".trivyignore.yaml accepts {id} without a statement of why.");

            Match expiry = Expiry().Match(entry);
            Assert.True(expiry.Success, $".trivyignore.yaml accepts {id} with no expired_at date.");
            Assert.True(
                DateOnly.TryParseExact(expiry.Groups["date"].Value, "yyyy-MM-dd", out _),
                $".trivyignore.yaml gives {id} an expiry that is not a date.");

            Assert.False(
                entry.Contains('*', StringComparison.Ordinal),
                $".trivyignore.yaml accepts {id} by a wildcard.");
        }
    }

    private static FileInfo[] Workflows() =>
        new DirectoryInfo(RepositoryLayout.ProjectFile(".github/workflows")).GetFiles("*.yml");

    [GeneratedRegex(@"uses:\s+[\w.-]+/[\w./-]+@[0-9a-f]{40} # v\d+(\.\d+)*$")]
    private static partial Regex PinnedAction();

    [GeneratedRegex(@"^[A-Z_]+_IMAGE:\s")]
    private static partial Regex ImageVariable();

    [GeneratedRegex(@":\S+@sha256:[0-9a-f]{64}$")]
    private static partial Regex DigestPinned();

    [GeneratedRegex(@"^[0-9a-f]{40}:[^*:]+:[a-z0-9-]+:\d+$")]
    private static partial Regex GitleaksFingerprint();

    [GeneratedRegex(@"^\s*- id:", RegexOptions.Multiline)]
    private static partial Regex EntryStart();

    [GeneratedRegex(@"expired_at:\s*(?<date>\S+)")]
    private static partial Regex Expiry();
}
