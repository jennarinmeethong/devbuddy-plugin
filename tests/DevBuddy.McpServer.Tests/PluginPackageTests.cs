using System.Text.Json;
using System.Text.RegularExpressions;
using DevBuddy.Application;
using DevBuddy.Application.Pipeline;

namespace DevBuddy.McpServer.Tests;

/// <summary>
/// The two plugin packages, checked against the surface they claim to describe.
/// <para>
/// Without this they are prose, and prose drifts: an operation gets renamed, a command keeps
/// calling the old name, and nobody notices until somebody runs it. Worse, a package could name a
/// human-gated operation and teach a model that it exists — which is precisely what "absent from
/// the tool list, not merely refused" was for.
/// </para>
/// <para>
/// Checked from the files rather than from a copy of their contents, so editing a package is what
/// runs the check.
/// </para>
/// </summary>
public sealed partial class PluginPackageTests
{
    private static readonly string[] Packages = ["claude", "codex"];

    [Fact]
    public void no_plugin_file_names_an_operation_that_people_alone_may_perform()
    {
        HashSet<string> humanOnly =
        [
            .. UseCaseCatalog.All
                .Where(descriptor => descriptor.AiExposure == AiExposure.Denied)
                .Select(descriptor => descriptor.Name)
        ];

        Assert.NotEmpty(humanOnly);

        List<string> offences = [];

        foreach (FileInfo file in PluginFiles())
        {
            string text = File.ReadAllText(file.FullName);

            offences.AddRange(
                humanOnly
                    .Where(name => text.Contains(name, StringComparison.Ordinal))
                    .Select(name => $"{file.Name} names {name}"));
        }

        // Not even as an example, and not even to say it is unavailable. A model reads these
        // files; a name in one is a name it now knows to try.
        Assert.Empty(offences);
    }

    [Fact]
    public void every_operation_a_plugin_file_names_is_one_the_server_exposes()
    {
        HashSet<string> exposed = [.. UseCaseCatalog.AiExposed.Select(descriptor => descriptor.Name)];
        List<string> unknown = [];

        foreach (FileInfo file in PluginFiles())
        {
            string text = File.ReadAllText(file.FullName);

            foreach (Match match in OperationName().Matches(text))
            {
                string candidate = match.Groups["name"].Value;

                if (!exposed.Contains(candidate))
                {
                    unknown.Add($"{file.Name} names {candidate}, which no tool is called");
                }
            }
        }

        Assert.Empty(unknown);
    }

    /// <summary>
    /// Both packages describe the same eighteen tools. Instructions differ between them on
    /// purpose; capability does not, and a package that listed seventeen would be teaching one
    /// assistant that the system is smaller than it is.
    /// </summary>
    [Fact]
    public void both_packages_describe_the_same_tool_surface()
    {
        string[] expected = [.. UseCaseCatalog.AiExposed.Select(descriptor => descriptor.Name).Order(StringComparer.Ordinal)];

        foreach (string package in Packages)
        {
            string instructions = File.ReadAllText(InstructionsFor(package).FullName);

            string[] named =
            [
                .. OperationName().Matches(instructions)
                    .Select(match => match.Groups["name"].Value)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
            ];

            Assert.Equal(expected, named);
        }
    }

    [Fact]
    public void both_packages_launch_the_same_server_over_stdio()
    {
        string claude = File.ReadAllText(Path.Combine(PackageRoot("claude").FullName, ".mcp.json"));
        string codex = File.ReadAllText(Path.Combine(PackageRoot("codex").FullName, "config.toml"));

        foreach (string configuration in new[] { claude, codex })
        {
            Assert.Contains("DevBuddy.McpServer", configuration, StringComparison.Ordinal);
            Assert.Contains("--stdio", configuration, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Identity comes from a credential, never from a claim.
    /// <para>
    /// Before Phase 9 the stdio server read the caller identifier from <c>DEVBUDDY_ACTOR</c>, which
    /// anybody who could start the process could set to anybody. Shipping plugin packages that
    /// documented it would have shipped impersonation as configuration, so the name is banned from
    /// them and a token is required instead.
    /// </para>
    /// </summary>
    [Fact]
    public void both_packages_configure_a_token_and_never_an_actor_identifier()
    {
        foreach (FileInfo file in PluginFiles())
        {
            Assert.DoesNotContain(
                "DEVBUDDY_ACTOR",
                File.ReadAllText(file.FullName),
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "DEVBUDDY_TOKEN",
            File.ReadAllText(Path.Combine(PackageRoot("claude").FullName, ".mcp.json")),
            StringComparison.Ordinal);

        Assert.Contains(
            "DEVBUDDY_TOKEN",
            File.ReadAllText(Path.Combine(PackageRoot("codex").FullName, "config.toml")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Both packages configure all four settings the server needs.
    /// <para>
    /// The Claude package named three and left <c>DEVBUDDY_Analysis__RootPath</c> to ordinary
    /// environment inheritance, which worked and was invisible until it did not: the server fell
    /// back to a path relative to its own working directory and every <c>analyze_*</c> call
    /// answered that there was nothing to analyse. A missing setting that degrades into a plausible
    /// empty answer is worse than one that fails, so the package declares it and this asserts it.
    /// </para>
    /// </summary>
    [Fact]
    public void both_packages_configure_every_setting_the_server_needs()
    {
        string[] settings =
        [
            "DEVBUDDY_ConnectionStrings__DevBuddy",
            "DEVBUDDY_Identity__SigningKey",
            "DEVBUDDY_TOKEN",
            "DEVBUDDY_Analysis__RootPath",
        ];

        (string Package, string Path)[] configurations =
        [
            ("claude", Path.Combine(PackageRoot("claude").FullName, ".mcp.json")),
            ("codex", Path.Combine(PackageRoot("codex").FullName, "config.toml")),
        ];

        foreach ((string package, string path) in configurations)
        {
            string configuration = File.ReadAllText(path);

            foreach (string setting in settings)
            {
                Assert.True(
                    configuration.Contains(setting, StringComparison.Ordinal),
                    $"The {package} package does not configure {setting}.");
            }
        }
    }

    [Fact]
    public void the_claude_manifest_is_valid_and_names_the_plugin()
    {
        var manifest = new FileInfo(
            Path.Combine(PackageRoot("claude").FullName, ".claude-plugin", "plugin.json"));

        Assert.True(manifest.Exists, $"{manifest.FullName} is missing.");

        JsonElement content = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(manifest.FullName));

        Assert.Equal("devbuddy", content.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(content.GetProperty("description").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(content.GetProperty("version").GetString()));
    }

    [Fact]
    public void every_slash_command_declares_what_it_is_for()
    {
        FileInfo[] commands = [.. PackageRoot("claude").GetDirectories("commands")[0].EnumerateFiles("*.md")];

        Assert.NotEmpty(commands);

        foreach (FileInfo command in commands)
        {
            string text = File.ReadAllText(command.FullName);

            Assert.StartsWith("---", text, StringComparison.Ordinal);
            Assert.Contains("description:", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Both packages have to say that the tool boundary is not the host's boundary.
    /// <para>
    /// MCP filtering governs DevBuddy and nothing else: the assistant's own file reads and shell
    /// commands go straight past it. An operator who assumed otherwise would leave a repository
    /// readable that they believed was not, so both instruction files say so and this checks they
    /// still do.
    /// </para>
    /// </summary>
    [Fact]
    public void both_packages_say_the_tool_boundary_is_not_the_host_boundary()
    {
        foreach (string package in Packages)
        {
            string instructions = File.ReadAllText(InstructionsFor(package).FullName);

            Assert.Contains("shell commands", instructions, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not filtered by it", instructions, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void both_packages_say_prompt_text_is_not_a_security_boundary()
    {
        foreach (string package in Packages)
        {
            string instructions = File.ReadAllText(InstructionsFor(package).FullName);

            Assert.Contains("security boundary", instructions, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("If a tool refuses", instructions, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The file a host reads for behaviour: a skill for Claude, AGENTS.md for Codex.</summary>
    private static FileInfo InstructionsFor(string package) => package switch
    {
        "claude" => new FileInfo(Path.Combine(
            PackageRoot("claude").FullName, "skills", "devbuddy", "SKILL.md")),
        "codex" => new FileInfo(Path.Combine(PackageRoot("codex").FullName, "AGENTS.md")),
        _ => throw new ArgumentOutOfRangeException(nameof(package)),
    };

    private static IEnumerable<FileInfo> PluginFiles() =>
        Packages.SelectMany(package => PackageRoot(package).EnumerateFiles("*", SearchOption.AllDirectories));

    private static DirectoryInfo PackageRoot(string package) =>
        new(Path.Combine(RepositoryRoot().FullName, "plugins", package));

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !directory.EnumerateFiles("DevBuddy.slnx").Any())
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("Could not find the repository root.");
    }

    /// <summary>
    /// An operation name as these files write one: in backticks, snake_case. Narrow on purpose —
    /// matching bare prose would turn every sentence containing "create" into a finding.
    /// </summary>
    [GeneratedRegex(@"`(?<name>[a-z][a-z0-9]*(?:_[a-z0-9]+)+)`")]
    private static partial Regex OperationName();
}
