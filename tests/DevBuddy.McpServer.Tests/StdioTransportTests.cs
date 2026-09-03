using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DevBuddy.Application;

namespace DevBuddy.McpServer.Tests;

/// <summary>
/// The stdio transport, spoken for real: a process, two pipes, and the MCP handshake.
/// <para>
/// Everything else in this project tests the tool surface in memory, which proves what the surface
/// is and nothing about whether a plugin can reach it. This launches the server the way Claude
/// Code and Codex do and talks to it over the pipes, which is the only way to catch the failures
/// that live in the transport rather than in the logic — a logger writing to stdout and corrupting
/// the protocol being the obvious one, and one this found.
/// </para>
/// <para>
/// No database is needed for what is checked here. Listing tools is a question about the
/// catalogue, and the connection string is configured rather than connected to, so a deliberately
/// unreachable one is enough to prove the process starts, negotiates, and answers. What a call
/// through this transport does is checked in <c>DevBuddy.Api.Tests</c>, against a real one:
/// refusing a denial without recording it would not be a refusal, so even being turned away needs
/// somewhere to write.
/// </para>
/// </summary>
public sealed class StdioTransportTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task the_server_negotiates_and_lists_its_tools_over_stdio()
    {
        using Process server = Launch();

        try
        {
            await SendAsync(server, 1, "initialize", new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "devbuddy-stdio-test", version = "1.0.0" },
            });

            JsonElement handshake = await ReadResponseAsync(server, 1);

            Assert.Equal(
                "devbuddy",
                handshake.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());

            await SendAsync(server, null, "notifications/initialized", new { });
            await SendAsync(server, 2, "tools/list", new { });

            JsonElement listing = await ReadResponseAsync(server, 2);

            string[] tools =
            [
                .. listing.GetProperty("result").GetProperty("tools").EnumerateArray()
                    .Select(tool => tool.GetProperty("name").GetString()!)
            ];

            // The same eighteen the in-memory surface exposes. Over a real transport, from a real
            // process, which is what a plugin will see.
            Assert.Equal(
                UseCaseCatalog.AiExposed.Select(descriptor => descriptor.Name),
                tools);
        }
        finally
        {
            Stop(server);
        }
    }

    private static Process Launch()
    {
        FileInfo assembly = ServerAssembly();

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = assembly.DirectoryName!,
        };

        start.ArgumentList.Add(assembly.FullName);
        start.ArgumentList.Add("--stdio");

        // Configured, not reachable. Enough for the host to compose itself; anything that actually
        // queried would fail loudly, which is what makes the refusal above meaningful.
        start.Environment["DEVBUDDY_ConnectionStrings__DevBuddy"] =
            "Host=devbuddy-nowhere.invalid;Database=devbuddy;Username=devbuddy;Password=none";

        start.Environment["DEVBUDDY_Identity__SigningKey"] =
            "stdio-transport-test-signing-key-not-for-production";

        start.Environment["DEVBUDDY_TOKEN"] = string.Empty;

        return Process.Start(start)
            ?? throw new InvalidOperationException("The MCP server process did not start.");
    }

    private static void Stop(Process server)
    {
        try
        {
            if (!server.HasExited)
            {
                server.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone. Nothing to do, and nothing worth failing a test over.
        }
    }

    private static async Task SendAsync(Process server, int? id, string method, object parameters)
    {
        var message = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters,
        };

        if (id is { } value)
        {
            message["id"] = value;
        }

        await server.StandardInput.WriteAsync(JsonSerializer.Serialize(message));
        await server.StandardInput.WriteAsync('\n');
        await server.StandardInput.FlushAsync();
    }

    /// <summary>
    /// Reads until the response with this identifier arrives.
    /// <para>
    /// Lines that are not it are skipped rather than failing: a server may send notifications, and
    /// a test that broke on one would be testing message ordering rather than the transport. A
    /// line that is not JSON at all does fail, because that is stdout being written to by
    /// something that should be using stderr.
    /// </para>
    /// </summary>
    private static async Task<JsonElement> ReadResponseAsync(Process server, int id)
    {
        using var timeout = new CancellationTokenSource(Patience);

        while (!timeout.IsCancellationRequested)
        {
            string? line;

            try
            {
                line = await server.StandardOutput.ReadLineAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonElement message;

            try
            {
                message = JsonSerializer.Deserialize<JsonElement>(line);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException(
                    $"The server wrote something other than a JSON-RPC message to stdout, which "
                    + $"corrupts the protocol: {Excerpt(line)}");
            }

            if (message.TryGetProperty("id", out JsonElement identifier)
                && identifier.ValueKind == JsonValueKind.Number
                && identifier.GetInt32() == id)
            {
                return message;
            }
        }

        string diagnostics = await ReadAvailableErrorAsync(server);

        throw new InvalidOperationException(
            $"No response to request {id} arrived within {Patience.TotalSeconds:0} seconds. "
            + $"Server error output: {Excerpt(diagnostics)}");
    }

    private static async Task<string> ReadAvailableErrorAsync(Process server)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var text = new StringBuilder();

        try
        {
            while (await server.StandardError.ReadLineAsync(timeout.Token) is { } line)
            {
                text.AppendLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Whatever arrived in two seconds is enough to say what went wrong.
        }

        return text.ToString();
    }

    private static string Excerpt(string text) =>
        text.Length <= 2000 ? text : text[..2000] + "…";

    /// <summary>
    /// The built MCP server, from this test assembly's own build configuration, so a Debug run
    /// does not silently exercise a stale Release binary.
    /// </summary>
    private static FileInfo ServerAssembly()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        string framework = here.Name;
        string configuration = here.Parent?.Name ?? "Release";

        var assembly = new FileInfo(Path.Combine(
            RepositoryRoot().FullName,
            "src", "hosts", "DevBuddy.McpServer", "bin", configuration, framework,
            "DevBuddy.McpServer.dll"));

        Assert.True(
            assembly.Exists,
            $"{assembly.FullName} has not been built. This test runs the real host process.");

        return assembly;
    }

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
