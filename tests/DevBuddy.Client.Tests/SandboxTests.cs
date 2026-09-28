using System.Text.Json;

namespace DevBuddy.Client.Tests;

/// <summary>
/// An assistant runs shell commands in a sandbox that may not reach the credential store. On
/// 2026-09-28 Codex's sandbox refused the macOS keychain, <c>devbuddy show --json</c> threw a stack
/// trace, and Codex then called <c>list_projects</c> without a workspace. The helper itself was
/// fine, because the assistant runs it outside the sandbox.
/// </summary>
public sealed class SandboxTests : IDisposable
{
    private readonly ClientHarness _client = new();

    [Fact]
    public async Task show_json_answers_the_workspace_without_touching_the_store()
    {
        string checkout = await RegisteredAsync();
        _client.Store.Unreachable = true;

        int exit = await _client.RunAsync("show", checkout, "--json");

        Assert.Equal(Commands.Done, exit);
        using JsonDocument shown = JsonDocument.Parse(_client.Out.ToString());
        Assert.Equal(ClientHarness.Workspace, shown.RootElement.GetProperty("workspaceId").GetGuid());
        Assert.Equal($"{ClientHarness.Server}/mcp", shown.RootElement.GetProperty("mcpUrl").GetString());
        Assert.False(shown.RootElement.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task show_says_the_token_could_not_be_checked_rather_than_failing()
    {
        string checkout = await RegisteredAsync();
        _client.Store.Unreachable = true;

        Assert.Equal(Commands.Done, await _client.RunAsync("show", checkout));
        Assert.Contains("could not be checked here", _client.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task the_helper_sends_nothing_and_says_why_when_the_store_is_out_of_reach()
    {
        string checkout = await RegisteredAsync();
        _client.Store.Unreachable = true;

        int exit = await _client.RunAsync("mcp-headers", "--dir", checkout, "--url", $"{ClientHarness.Server}/mcp");

        Assert.Equal(Commands.NoToken, exit);
        Assert.Equal(string.Empty, _client.Out.ToString());
        Assert.Contains("credential store could not be read", _client.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_failure_anywhere_else_is_one_sentence_and_not_a_stack_trace()
    {
        _client.Store.Unreachable = true;
        _client.Tokens.Enqueue(ClientHarness.GoodToken);

        int exit = await _client.RunAsync(
            "register", _client.Checkout("api"), "--server", ClientHarness.Server, "--workspace", ClientHarness.Workspace.ToString());

        Assert.Equal(Commands.Failed, exit);
        Assert.StartsWith("DevBuddy: The keychain", _client.Error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", _client.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task list_says_it_is_registrations_and_where_projects_come_from()
    {
        await RegisteredAsync();

        Assert.Equal(Commands.Done, await _client.RunAsync("list"));
        Assert.Contains("list_projects", _client.Out.ToString(), StringComparison.Ordinal);
    }

    public void Dispose() => _client.Dispose();

    private async Task<string> RegisteredAsync()
    {
        string checkout = _client.Checkout("api");
        _client.Remote.Accepted.Add(ClientHarness.GoodToken);
        _client.Tokens.Enqueue(ClientHarness.GoodToken);

        int exit = await _client.RunAsync(
            "register", checkout, "--server", ClientHarness.Server, "--workspace", ClientHarness.Workspace.ToString());

        Assert.True(exit == Commands.Done, _client.Error.ToString());
        return checkout;
    }
}
