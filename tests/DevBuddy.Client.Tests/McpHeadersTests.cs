using System.Text.Json;

namespace DevBuddy.Client.Tests;

/// <summary>
/// The header helper, which is where the token leaves this machine. It sends it only for a
/// registered checkout, and only to the server that checkout is registered to (ADR-0015).
/// </summary>
public sealed class McpHeadersTests : IDisposable
{
    private const string McpUrl = ClientHarness.Server + "/mcp";

    private readonly ClientHarness _client = new();

    [Fact]
    public async Task a_registered_checkout_gets_its_workspaces_token_as_the_bearer()
    {
        string checkout = await RegisteredAsync("api");

        int exit = await _client.RunAsync("mcp-headers", "--dir", checkout, "--url", McpUrl);

        Assert.Equal(Commands.Done, exit);
        using JsonDocument headers = JsonDocument.Parse(_client.Out.ToString());
        Assert.Equal($"Bearer {ClientHarness.GoodToken}", headers.RootElement.GetProperty("Authorization").GetString());
    }

    [Fact]
    public async Task a_folder_inside_the_checkout_is_the_checkout()
    {
        string checkout = await RegisteredAsync("api");
        string inside = Directory.CreateDirectory(Path.Combine(checkout, "src", "deep")).FullName;

        Assert.Equal(Commands.Done, await _client.RunAsync("mcp-headers", "--dir", inside, "--url", McpUrl));
    }

    /// <summary>
    /// Claude Code runs a plugin's helper in the plugin's own folder, so the plugin passes the
    /// project folder and the URL arrives in the environment.
    /// </summary>
    [Fact]
    public async Task claude_codes_url_variable_is_read_when_no_url_is_given()
    {
        string checkout = await RegisteredAsync("api");
        _client.SetEnvironment("CLAUDE_CODE_MCP_SERVER_URL", McpUrl);

        Assert.Equal(Commands.Done, await _client.RunAsync("mcp-headers", "--dir", checkout));
    }

    [Fact]
    public async Task the_working_directory_is_the_folder_when_none_is_given()
    {
        string checkout = await RegisteredAsync("api");
        _client.WorkingDirectory = checkout;

        Assert.Equal(Commands.Done, await _client.RunAsync("mcp-headers", "--url", McpUrl));
    }

    [Fact]
    public async Task an_unregistered_folder_gets_nothing_and_is_told_to_register()
    {
        await RegisteredAsync("api");
        string stranger = _client.Checkout("somebody-elses");

        int exit = await _client.RunAsync("mcp-headers", "--dir", stranger, "--url", McpUrl);

        Assert.Equal(Commands.NotRegistered, exit);
        Assert.Equal(string.Empty, _client.Out.ToString());
        Assert.Contains("devbuddy register", _client.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The check that matters most: a configuration naming another server, by mistake or because
    /// somebody changed it, is given nothing.
    /// </summary>
    [Theory]
    [InlineData("https://attacker.example.test/mcp")]
    [InlineData("https://devbuddy.example.test:5011/mcp")]
    [InlineData("http://devbuddy.example.test:5010/mcp")]
    [InlineData("")]
    public async Task a_url_that_is_not_the_registered_server_gets_nothing(string url)
    {
        string checkout = await RegisteredAsync("api");

        int exit = await _client.RunAsync("mcp-headers", "--dir", checkout, "--url", url);

        Assert.Equal(Commands.WrongServer, exit);
        Assert.Equal(string.Empty, _client.Out.ToString());
        Assert.DoesNotContain(ClientHarness.GoodToken, _client.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_registered_checkout_whose_token_was_removed_gets_nothing()
    {
        string checkout = await RegisteredAsync("api");
        _client.Store.Delete(CredentialStores.KeyFor(ClientHarness.Server, ClientHarness.Workspace));

        int exit = await _client.RunAsync("mcp-headers", "--dir", checkout, "--url", McpUrl);

        Assert.Equal(Commands.NoToken, exit);
        Assert.Equal(string.Empty, _client.Out.ToString());
    }

    /// <summary>A checkout registered inside another one's folder is found for its own files.</summary>
    [Fact]
    public async Task the_innermost_registered_checkout_wins()
    {
        string outer = await RegisteredAsync("outer");
        string inner = Path.Combine(outer, "vendor", "inner");
        Directory.CreateDirectory(Path.Combine(inner, ".git"));

        const string otherServer = "https://other.example.test";
        string otherToken = new('B', 43);
        _client.Store.Write(CredentialStores.KeyFor(otherServer, ClientHarness.Workspace), otherToken);

        var registry = Registry.Load(_client.Home);
        registry.Put(new Checkout(inner, otherServer, ClientHarness.Workspace, null));
        registry.Save();

        Assert.Equal(Commands.Done, await _client.RunAsync("mcp-headers", "--dir", inner, "--url", otherServer + "/mcp"));
        Assert.Contains(otherToken, _client.Out.ToString(), StringComparison.Ordinal);

        Assert.Equal(Commands.WrongServer, await _client.RunAsync("mcp-headers", "--dir", inner, "--url", McpUrl));
    }

    public void Dispose() => _client.Dispose();

    private async Task<string> RegisteredAsync(string name)
    {
        string checkout = _client.Checkout(name);
        _client.Remote.Accepted.Add(ClientHarness.GoodToken);
        _client.Tokens.Enqueue(ClientHarness.GoodToken);

        int exit = await _client.RunAsync(
            "register", checkout, "--server", ClientHarness.Server, "--workspace", ClientHarness.Workspace.ToString());

        Assert.True(exit == Commands.Done, _client.Error.ToString());
        return checkout;
    }
}
