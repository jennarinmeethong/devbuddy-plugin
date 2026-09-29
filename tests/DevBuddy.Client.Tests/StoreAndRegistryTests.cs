namespace DevBuddy.Client.Tests;

/// <summary>What the client keeps on disk, and how it compares folders and servers.</summary>
public sealed class StoreAndRegistryTests : IDisposable
{
    private readonly ClientHarness _client = new();

    [Fact]
    public void the_file_store_round_trips_and_names_no_server_or_workspace_on_disk()
    {
        string key = CredentialStores.KeyFor(ClientHarness.Server, ClientHarness.Workspace);

        _client.Store.Write(key, ClientHarness.GoodToken);

        Assert.Equal(ClientHarness.GoodToken, _client.Store.Read(key));

        string file = Directory.EnumerateFiles(_client.Home.TokenDirectory).Single();
        Assert.DoesNotContain("devbuddy.example", Path.GetFileName(file), StringComparison.Ordinal);
        Assert.DoesNotContain(ClientHarness.Workspace.ToString(), Path.GetFileName(file), StringComparison.Ordinal);

        Assert.True(_client.Store.Delete(key));
        Assert.Null(_client.Store.Read(key));
        Assert.False(_client.Store.Delete(key));
    }

    [Fact]
    public void on_unix_the_token_file_and_the_registry_are_readable_by_their_owner_alone()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        _client.Store.Write(CredentialStores.KeyFor(ClientHarness.Server, ClientHarness.Workspace), ClientHarness.GoodToken);

        var registry = Registry.Load(_client.Home);
        registry.Put(new Checkout(_client.Checkout("api"), ClientHarness.Server, ClientHarness.Workspace, null));
        registry.Save();

        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        Assert.Equal(ownerOnly, File.GetUnixFileMode(Directory.EnumerateFiles(_client.Home.TokenDirectory).Single()));
        Assert.Equal(ownerOnly, File.GetUnixFileMode(_client.Home.RegistryFile));
        Assert.Equal(ownerOnly | UnixFileMode.UserExecute, File.GetUnixFileMode(_client.Home.Directory));
    }

    [Fact]
    public void the_registry_survives_a_save_and_a_load()
    {
        string api = _client.Checkout("api");
        var project = Guid.NewGuid();

        var registry = Registry.Load(_client.Home);
        registry.Put(new Checkout(api, ClientHarness.Server, ClientHarness.Workspace, project));
        registry.Save();

        Checkout loaded = Registry.Load(_client.Home).All.Single();
        Assert.True(Folders.Same(Folders.Normalise(api), loaded.Path));
        Assert.Equal(ClientHarness.Server, loaded.Server);
        Assert.Equal(project, loaded.Project);
    }

    [Fact]
    public void a_folder_named_like_the_checkout_but_beside_it_is_not_inside_it()
    {
        string api = _client.Checkout("api");
        string beside = _client.Checkout("api-old");

        var registry = Registry.Load(_client.Home);
        registry.Put(new Checkout(api, ClientHarness.Server, ClientHarness.Workspace, null));

        Assert.Null(registry.Find(beside));
        Assert.NotNull(registry.Find(Path.Combine(api, "src")));
    }

    [Fact]
    public void on_windows_and_macos_the_same_folder_in_another_case_is_the_same_checkout()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        string api = _client.Checkout("Api");
        var registry = Registry.Load(_client.Home);
        registry.Put(new Checkout(api, ClientHarness.Server, ClientHarness.Workspace, null));

        Assert.NotNull(registry.Find(api.ToUpperInvariant()));
    }

    [Fact]
    public void a_worktree_whose_git_entry_is_a_file_is_a_repository()
    {
        string worktree = Directory.CreateDirectory(Path.Combine(_client.Root, "worktree", "src")).FullName;
        File.WriteAllText(Path.Combine(_client.Root, "worktree", ".git"), "gitdir: elsewhere");

        Assert.True(Folders.Same(
            Folders.Normalise(Path.Combine(_client.Root, "worktree")),
            Folders.GitRoot(worktree)!));
    }

    [Fact]
    public void a_folder_is_too_broad_at_or_above_home_and_at_a_drive_root_and_nowhere_else()
    {
        string home = Directory.CreateDirectory(Path.Combine(_client.Root, "people", "someone")).FullName;

        Assert.Equal("your home folder", Folders.TooBroad(home, home));
        Assert.Equal("above your home folder", Folders.TooBroad(Path.Combine(_client.Root, "people"), home));
        Assert.Equal("the root of a drive", Folders.TooBroad(Path.GetPathRoot(home)!, home));
        Assert.Null(Folders.TooBroad(Path.Combine(home, "Documents", "requirements"), home));
        Assert.Null(Folders.TooBroad(Path.Combine(_client.Root, "people", "someone-else"), home));
    }

    [Theory]
    [InlineData("https://192.168.1.160:5010", "https://192.168.1.160:5010")]
    [InlineData("https://192.168.1.160:5010/", "https://192.168.1.160:5010")]
    [InlineData("HTTPS://DevBuddy.Example.Test", "https://devbuddy.example.test")]
    [InlineData("https://devbuddy.example.test:443/mcp", "https://devbuddy.example.test")]
    [InlineData("http://127.0.0.1:5010", "http://127.0.0.1:5010")]
    [InlineData("http://localhost:5010", "http://localhost:5010")]
    public void a_server_is_recorded_as_its_origin(string given, string origin)
    {
        Assert.True(ServerOrigin.TryParse(given, out string parsed, out _));
        Assert.Equal(origin, parsed);
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQ", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz-_0123456789ABCDE", true)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQQ", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/Q", false)]
    public void a_token_is_exactly_43_base64url_characters(string token, bool valid) =>
        Assert.Equal(valid, TokenFormat.IsValid(token));

    /// <summary>
    /// The real Credential Manager, on Windows only, with a key no person would ever use, removed
    /// again whatever happens.
    /// </summary>
    [Fact]
    public void windows_credential_manager_round_trips_a_token()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new WindowsCredentialStore();
        string key = $"devbuddy:test/{Guid.NewGuid():D}";

        try
        {
            Assert.Null(store.Read(key));

            try
            {
                store.Write(key, ClientHarness.GoodToken);
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("error 1312", StringComparison.Ordinal))
            {
                // ERROR_NO_SUCH_LOGON_SESSION: a service account with no interactive logon, as a
                // hosted CI runner may be, has no Credential Manager to write to. That says nothing
                // about the store, only about where it ran; a person's session always has one.
                return;
            }

            Assert.Equal(ClientHarness.GoodToken, store.Read(key));

            store.Write(key, new string('Z', 43));
            Assert.Equal(new string('Z', 43), store.Read(key));
        }
        finally
        {
            store.Delete(key);
        }

        Assert.Null(store.Read(key));
    }

    public void Dispose() => _client.Dispose();
}
