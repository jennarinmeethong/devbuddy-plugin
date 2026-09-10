using System.Text.Json;

namespace DevBuddy.McpServer.Tests;

/// <summary>
/// The workspace template in <c>templates/devbuddy-root/</c>, checked for the mistakes that would
/// make shipping it worse than not shipping it.
/// <para>
/// It is not product code and nothing reads it at run time, so the only thing standing between it
/// and a committed credential is this file. The template exists to be copied and filled in; a copy
/// that arrived already filled in would put one person's token, or one company's identifiers, into
/// everybody else's checkout.
/// </para>
/// </summary>
public sealed class WorkspaceTemplateTests
{
    /// <summary>
    /// Keys whose value in the example must be empty. The others carry a label or a path, which is
    /// the point of an example.
    /// </summary>
    private static readonly string[] MustBeBlank =
    [
        "DEVBUDDY_CONNECTION_STRING",
        "DEVBUDDY_SIGNING_KEY",
        "DEVBUDDY_TOKEN",
    ];

    [Fact]
    public void the_example_environment_file_carries_no_secret()
    {
        Dictionary<string, string> values = ReadEnvironmentFile(
            Path.Combine(TemplateRoot().FullName, ".devbuddy", "deployment.env.example"));

        foreach (string key in MustBeBlank)
        {
            Assert.True(values.ContainsKey(key), $"{key} is missing from deployment.env.example.");
            Assert.True(
                values[key].Length == 0,
                $"{key} has a value in deployment.env.example. The example is committed; fill it in only in deployment.env.");
        }
    }

    /// <summary>
    /// The example names the workspace its token belongs to, as the placeholder.
    /// <para>
    /// A machine token works in one DevBuddy workspace, so the file holding the credential has to
    /// say which one — that is what lets <c>enter.ps1</c> refuse a <c>deployment.env</c> copied in
    /// from another root, instead of the mismatch surfacing later as a refusal from the server
    /// partway through somebody's work.
    /// </para>
    /// </summary>
    [Fact]
    public void the_example_names_the_workspace_its_token_belongs_to()
    {
        Dictionary<string, string> values = ReadEnvironmentFile(
            Path.Combine(TemplateRoot().FullName, ".devbuddy", "deployment.env.example"));

        Assert.True(
            values.ContainsKey("DEVBUDDY_WORKSPACE_ID"),
            "DEVBUDDY_WORKSPACE_ID is missing from deployment.env.example.");

        Assert.Equal("00000000-0000-0000-0000-000000000000", values["DEVBUDDY_WORKSPACE_ID"]);
    }

    /// <summary>
    /// The two guards that make a per-root arrangement mean anything, present in the shared
    /// script rather than only in the document that explains them.
    /// </summary>
    [Fact]
    public void the_scripts_refuse_a_mismatched_workspace_and_a_persistent_setting()
    {
        string common = File.ReadAllText(
            Path.Combine(TemplateRoot().FullName, ".devbuddy", "common.ps1"));

        string enter = File.ReadAllText(
            Path.Combine(TemplateRoot().FullName, ".devbuddy", "enter.ps1"));

        foreach (string guard in new[]
        {
            "Assert-DevBuddyWorkspaceMatches",
            "Assert-NoPersistentDevBuddySettings",
        })
        {
            Assert.Contains(guard, common, StringComparison.Ordinal);
            Assert.Contains(guard, enter, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Only the example is committed. A real <c>deployment.env</c> is refused by the root
    /// <c>.gitignore</c>, but a file that never gets written cannot be ignored by mistake either.
    /// </summary>
    [Fact]
    public void no_filled_in_environment_file_is_committed()
    {
        Assert.False(
            File.Exists(Path.Combine(TemplateRoot().FullName, ".devbuddy", "deployment.env")),
            "templates/devbuddy-root/.devbuddy/deployment.env exists. Only deployment.env.example belongs here.");
    }

    /// <summary>
    /// Every identifier ships as the all-zero placeholder, and <c>setup.ps1</c> refuses that value,
    /// so a copy cannot be run until somebody has put the real ones in.
    /// </summary>
    [Fact]
    public void every_identifier_in_the_template_is_still_a_placeholder()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(TemplateRoot().FullName, ".devbuddy", "project.json")));

        JsonElement root = document.RootElement;

        AssertPlaceholder(root, "workspaceId");
        AssertPlaceholder(root, "projectId");

        JsonElement repositories = root.GetProperty("repositories");
        Assert.NotEqual(0, repositories.GetArrayLength());

        foreach (JsonElement repository in repositories.EnumerateArray())
        {
            AssertPlaceholder(repository, "repositoryId");
            Assert.False(
                string.IsNullOrWhiteSpace(repository.GetProperty("path").GetString()),
                "A repositories entry in the template has no path.");
        }
    }

    /// <summary>
    /// The ignore file is committed as <c>gitignore</c> and copied into place as <c>.gitignore</c>.
    /// <para>
    /// Named with the dot here it would apply to this repository, and its one rule is
    /// <c>.devbuddy/</c> — so git would ignore the template's own contents and the thing meant to
    /// be copied would never be committed at all.
    /// </para>
    /// </summary>
    [Fact]
    public void the_ignore_file_is_committed_without_its_leading_dot()
    {
        DirectoryInfo template = TemplateRoot();

        Assert.True(
            File.Exists(Path.Combine(template.FullName, "gitignore")),
            "templates/devbuddy-root/gitignore is missing.");

        Assert.False(
            File.Exists(Path.Combine(template.FullName, ".gitignore")),
            "templates/devbuddy-root/.gitignore would make git ignore the template's own .devbuddy directory.");
    }

    [Fact]
    public void the_template_is_documented()
    {
        Assert.True(
            File.Exists(Path.Combine(
                RepositoryRoot().FullName, "docs", "operations", "workspace-layout.md")),
            "The template has no operations document explaining it.");
    }

    private static void AssertPlaceholder(JsonElement element, string property)
    {
        string? value = element.GetProperty(property).GetString();

        Assert.Equal("00000000-0000-0000-0000-000000000000", value);
    }

    private static Dictionary<string, string> ReadEnvironmentFile(string path)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);

        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            int split = trimmed.IndexOf('=', StringComparison.Ordinal);

            if (split < 1)
            {
                continue;
            }

            values[trimmed[..split].Trim()] = trimmed[(split + 1)..].Trim();
        }

        return values;
    }

    private static DirectoryInfo TemplateRoot() =>
        new(Path.Combine(RepositoryRoot().FullName, "templates", "devbuddy-root"));

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !directory.EnumerateFiles("DevBuddy.slnx").Any())
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
