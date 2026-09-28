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

    /// <summary>
    /// Both packages reach the server over HTTP at <c>/mcp</c>, and take the token from the
    /// <c>devbuddy</c> client's header helper (Phase 14, A4; ADR-0015).
    /// <para>
    /// Claude Code runs a plugin's helper in the plugin's own folder, so the Claude package passes
    /// the project folder. Both packages pass the URL to the helper themselves: Claude Code's
    /// <c>CLAUDE_CODE_MCP_SERVER_URL</c> arrived on JMPC with every <c>1</c> replaced by
    /// <c>REDACTED</c>, because it redacts the values of credential variables wherever they occur,
    /// and the helper rightly refused a URL that was not the registered server (2026-09-28). Codex's
    /// file names the URL twice, and the two must agree, because the helper hands the token only
    /// to the server the checkout is registered to.
    /// </para>
    /// </summary>
    [Fact]
    public void both_packages_connect_over_http_at_mcp_through_the_devbuddy_client()
    {
        using JsonDocument claude = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(PackageRoot("claude").FullName, ".mcp.json")));
        JsonElement server = claude.RootElement.GetProperty("mcpServers").GetProperty("devbuddy");

        Assert.Equal("http", server.GetProperty("type").GetString());
        Assert.EndsWith("/mcp", server.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Contains("mcp-headers", server.GetProperty("headersHelper").GetString(), StringComparison.Ordinal);
        Assert.Contains("${CLAUDE_PROJECT_DIR}", server.GetProperty("headersHelper").GetString(), StringComparison.Ordinal);
        Assert.Contains(
            $"--url \"{server.GetProperty("url").GetString()}\"",
            server.GetProperty("headersHelper").GetString(),
            StringComparison.Ordinal);
        Assert.False(server.TryGetProperty("command", out _), "The Claude package still launches a local server.");

        string codex = File.ReadAllText(Path.Combine(PackageRoot("codex").FullName, "config.toml"));
        string url = TomlString(codex, "url");
        string helper = TomlString(codex, "http_headers_helper");

        Assert.EndsWith("/mcp", url, StringComparison.Ordinal);
        Assert.Contains($"mcp-headers --url {url}", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("command =", codex, StringComparison.Ordinal);
    }

    /// <summary>
    /// Neither package holds a credential, or anything a server keeps secret.
    /// <para>
    /// The token lives in the operating system's credential store and reaches the connection
    /// through the helper. A token in a package would be copied into <c>~/.codex/config.toml</c>,
    /// one file for the whole operating-system account, or into a variable every command the
    /// assistant runs inherits. The database and the signing key were in these files while the
    /// server ran locally over stdio; a client of the HTTP transport needs neither, and must not be
    /// told them. <c>DEVBUDDY_ACTOR</c>, the pre-Phase 9 identity claim, stays banned.
    /// </para>
    /// </summary>
    [Fact]
    public void neither_package_holds_a_token_or_a_server_secret()
    {
        string[] banned =
        [
            "DEVBUDDY_ACTOR",
            "DEVBUDDY_TOKEN",
            "DEVBUDDY_ConnectionStrings__DevBuddy",
            "DEVBUDDY_CONNECTION_STRING",
            "DEVBUDDY_Identity__SigningKey",
            "DEVBUDDY_SIGNING_KEY",
            "bearer_token_env_var",
            "Bearer ",
        ];

        foreach (FileInfo file in PluginFiles())
        {
            string text = File.ReadAllText(file.FullName);

            foreach (string name in banned)
            {
                Assert.False(
                    text.Contains(name, StringComparison.Ordinal),
                    $"{file.Name} mentions {name}.");
            }
        }
    }

    /// <summary>
    /// Neither package assigns a value to any setting. A placeholder in a committed example becomes
    /// a real value the first time somebody fills it in locally and pastes their file back.
    /// </summary>
    [Fact]
    public void neither_package_assigns_a_value_to_any_devbuddy_setting()
    {
        List<string> offences = [];

        foreach (FileInfo file in PluginFiles())
        {
            if (!file.Name.EndsWith(".toml", StringComparison.Ordinal)
                && !file.Name.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string line in File.ReadAllLines(file.FullName))
            {
                Match assignment = SettingAssignment().Match(line);

                if (!assignment.Success)
                {
                    continue;
                }

                string value = assignment.Groups["value"].Value.Trim();

                // A JSON substitution is the value arriving from the environment, which is the
                // point. Anything else is a value living in a committed file.
                if (!value.StartsWith("\"${", StringComparison.Ordinal))
                {
                    offences.Add($"{file.Name} assigns {assignment.Groups["name"].Value}");
                }
            }
        }

        Assert.Empty(offences);
    }

    /// <summary>
    /// Both packages have to say what a token is bound to, because getting it wrong is a
    /// cross-company knowledge leak rather than an inconvenience.
    /// <para>
    /// Three claims, and the last is the one an assistant most needs: identity is fixed when the
    /// session starts, so changing directory into another workspace's checkout does not change
    /// what the tools reach. An assistant that believed otherwise would carry one company's
    /// recorded knowledge into another company's source file and think it was being helpful.
    /// </para>
    /// </summary>
    [Fact]
    public void both_packages_say_a_token_is_one_person_in_one_workspace()
    {
        foreach (string package in Packages)
        {
            string instructions = File.ReadAllText(InstructionsFor(package).FullName);

            Assert.Contains("one DevBuddy workspace", instructions, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("two tokens", instructions, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Changing directory", instructions, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// And neither package may claim the token has anything to do with the account the assistant
    /// itself signs in with. It does not: a DevBuddy token authenticates a DevBuddy user, the same
    /// one token works in either assistant, and believing otherwise would make somebody think
    /// switching assistants had changed who they were.
    /// </summary>
    [Fact]
    public void neither_package_ties_a_token_to_the_assistants_own_account()
    {
        foreach (string package in Packages)
        {
            string instructions = File.ReadAllText(InstructionsFor(package).FullName);

            Assert.Contains("not tied to", instructions, StringComparison.OrdinalIgnoreCase);
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
    /// <summary>The quoted value of a top-level key in the Codex file.</summary>
    private static string TomlString(string toml, string key)
    {
        Match match = Regex.Match(toml, $@"(?m)^{Regex.Escape(key)}\s*=\s*""(?<value>[^""]*)""");
        Assert.True(match.Success, $"config.toml has no {key}.");
        return match.Groups["value"].Value;
    }

    [GeneratedRegex(@"`(?<name>[a-z][a-z0-9]*(?:_[a-z0-9]+)+)`")]
    private static partial Regex OperationName();

    /// <summary>
    /// A setting being given a value, in either file's syntax: a bare TOML key or a quoted JSON
    /// property, an equals or a colon, and whatever follows. A name inside a list — which is how
    /// both packages now name what they need — has no separator after it and does not match.
    /// </summary>
    [GeneratedRegex(@"^\s*""?(?<name>DEVBUDDY_[A-Za-z0-9_]+)""?\s*[=:]\s*(?<value>.+)$")]
    private static partial Regex SettingAssignment();
}
