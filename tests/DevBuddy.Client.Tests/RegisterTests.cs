namespace DevBuddy.Client.Tests;

/// <summary>
/// Registering a checkout, and the token that comes with it: checked by the server before it is
/// stored, asked for once per workspace, never taken as an argument.
/// </summary>
public sealed class RegisterTests : IDisposable
{
    private readonly ClientHarness _client = new();

    [Fact]
    public async Task a_token_the_server_accepts_is_stored_and_the_checkout_registered()
    {
        string checkout = _client.Checkout("api");
        _client.Remote.Accepted.Add(ClientHarness.GoodToken);
        _client.Tokens.Enqueue(ClientHarness.GoodToken);

        Assert.Equal(Commands.Done, await RegisterAsync(checkout));

        Assert.Equal(
            ClientHarness.GoodToken,
            _client.Store.Read(CredentialStores.KeyFor(ClientHarness.Server, ClientHarness.Workspace)));
        Assert.NotNull(Registry.Load(_client.Home).Exact(checkout));
        Assert.DoesNotContain(ClientHarness.GoodToken, _client.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_second_checkout_in_the_same_workspace_is_not_asked_for_the_token_again()
    {
        _client.Remote.Accepted.Add(ClientHarness.GoodToken);
        _client.Tokens.Enqueue(ClientHarness.GoodToken);

        Assert.Equal(Commands.Done, await RegisterAsync(_client.Checkout("api")));
        Assert.Equal(Commands.Done, await RegisterAsync(Path.Combine(_client.Checkout("web"))));

        Assert.Equal(1, _client.TokenPrompts);
        Assert.Equal(2, Registry.Load(_client.Home).All.Count);
    }

    /// <summary>A paste that caught the words around the token, which happened on the devbox.</summary>
    [Theory]
    [InlineData("Token: AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQ")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("")]
    public async Task something_that_is_not_a_token_is_refused_before_the_server_is_asked(string pasted)
    {
        _client.Tokens.Enqueue(pasted);

        Assert.Equal(Commands.Failed, await RegisterAsync(_client.Checkout("api")));

        Assert.Empty(_client.Remote.Requests);
        Assert.Empty(Registry.Load(_client.Home).All);
    }

    [Fact]
    public async Task a_token_the_server_refuses_is_neither_stored_nor_registered()
    {
        _client.Tokens.Enqueue(ClientHarness.GoodToken);

        Assert.Equal(Commands.Failed, await RegisterAsync(_client.Checkout("api")));

        Assert.Null(_client.Store.Read(CredentialStores.KeyFor(ClientHarness.Server, ClientHarness.Workspace)));
        Assert.Empty(Registry.Load(_client.Home).All);
        Assert.Contains("refused the token", _client.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_token_from_another_workspace_is_named_as_such_and_not_stored()
    {
        _client.Remote.OtherWorkspace.Add(ClientHarness.GoodToken);
        _client.Tokens.Enqueue(ClientHarness.GoodToken);

        Assert.Equal(Commands.Failed, await RegisterAsync(_client.Checkout("api")));

        Assert.Contains("not in this workspace", _client.Error.ToString(), StringComparison.Ordinal);
        Assert.Null(_client.Store.Read(CredentialStores.KeyFor(ClientHarness.Server, ClientHarness.Workspace)));
    }

    [Fact]
    public async Task a_folder_outside_any_git_repository_is_refused()
    {
        string plain = Directory.CreateDirectory(Path.Combine(_client.Root, "not-a-repo")).FullName;

        Assert.Equal(Commands.Failed, await RegisterAsync(plain));
        Assert.Equal(0, _client.TokenPrompts);
    }

    [Theory]
    [InlineData("http://192.168.1.160:5010")]
    [InlineData("ftp://devbuddy.example.test")]
    [InlineData("https://user:pass@devbuddy.example.test")]
    [InlineData("not a url")]
    public async Task a_server_address_that_would_expose_the_token_is_refused(string server)
    {
        Assert.Equal(
            Commands.Failed,
            await _client.RunAsync(
                "register", _client.Checkout("api"), "--server", server, "--workspace", ClientHarness.Workspace.ToString()));

        Assert.Equal(0, _client.TokenPrompts);
    }

    [Fact]
    public async Task a_subfolder_registers_the_repository_that_holds_it()
    {
        string checkout = _client.Checkout("api");
        string inside = Directory.CreateDirectory(Path.Combine(checkout, "src")).FullName;
        _client.Remote.Accepted.Add(ClientHarness.GoodToken);
        _client.Tokens.Enqueue(ClientHarness.GoodToken);

        Assert.Equal(Commands.Done, await RegisterAsync(inside));

        Assert.True(Folders.Same(Folders.Normalise(checkout), Registry.Load(_client.Home).All.Single().Path));
    }

    [Fact]
    public async Task a_token_in_use_is_not_removed_without_force()
    {
        _client.Remote.Accepted.Add(ClientHarness.GoodToken);
        _client.Tokens.Enqueue(ClientHarness.GoodToken);
        await RegisterAsync(_client.Checkout("api"));

        string[] remove = ["token", "remove", "--server", ClientHarness.Server, "--workspace", ClientHarness.Workspace.ToString()];

        Assert.Equal(Commands.Failed, await _client.RunAsync(remove));
        Assert.NotNull(_client.Store.Read(CredentialStores.KeyFor(ClientHarness.Server, ClientHarness.Workspace)));

        Assert.Equal(Commands.Done, await _client.RunAsync([.. remove, "--force"]));
        Assert.Null(_client.Store.Read(CredentialStores.KeyFor(ClientHarness.Server, ClientHarness.Workspace)));
    }

    [Fact]
    public async Task unregistering_keeps_the_token_for_other_checkouts()
    {
        _client.Remote.Accepted.Add(ClientHarness.GoodToken);
        _client.Tokens.Enqueue(ClientHarness.GoodToken);
        string api = _client.Checkout("api");
        await RegisterAsync(api);

        Assert.Equal(Commands.Done, await _client.RunAsync("unregister", api));

        Assert.Empty(Registry.Load(_client.Home).All);
        Assert.NotNull(_client.Store.Read(CredentialStores.KeyFor(ClientHarness.Server, ClientHarness.Workspace)));
    }

    [Fact]
    public async Task update_moves_a_checkout_to_another_workspace_with_that_workspaces_token()
    {
        _client.Remote.Accepted.Add(ClientHarness.GoodToken);
        _client.Tokens.Enqueue(ClientHarness.GoodToken);
        string api = _client.Checkout("api");
        await RegisterAsync(api);

        var other = Guid.NewGuid();
        string otherToken = new('C', 43);
        _client.Remote.Accepted.Add(otherToken);
        _client.Tokens.Enqueue(otherToken);

        Assert.Equal(Commands.Done, await _client.RunAsync("update", api, "--workspace", other.ToString()));

        Assert.Equal(other, Registry.Load(_client.Home).Exact(api)!.Workspace);
        Assert.Equal(otherToken, _client.Store.Read(CredentialStores.KeyFor(ClientHarness.Server, other)));
    }

    public void Dispose() => _client.Dispose();

    private Task<int> RegisterAsync(string path) =>
        _client.RunAsync(
            "register", path, "--server", ClientHarness.Server, "--workspace", ClientHarness.Workspace.ToString());
}
