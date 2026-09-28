using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DevBuddy.Application;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Api.Tests;

/// <summary>
/// The MCP server's HTTP transport, as a plugin reaches it through the gateway (Phase 14, A4): a
/// server process on a port, a real database, and a machine token as the bearer.
/// <para>
/// The same four things <see cref="StdioPluginTests"/> proves for stdio — without a token nobody is
/// anybody, with one the caller is exactly its owner in exactly one workspace, and revoking one
/// stops it on the next call — plus what is particular to HTTP: the API's own access token is not
/// a credential here, a token in the server's own environment is never lent to a request, and the
/// rate limit counts per person.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HttpTransportTests(ApiFixture fixture)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task without_a_bearer_the_transport_answers_401_and_names_the_scheme()
    {
        await using Server server = await Server.StartAsync(fixture);

        using HttpResponseMessage response = await server.PostAsync(bearer: null, Initialize());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    /// <summary>
    /// Until A4 this transport took exactly this token. It lasts fifteen minutes and reaches every
    /// workspace its owner belongs to, which is why it is refused now like any other stranger.
    /// </summary>
    [Fact]
    public async Task the_apis_own_access_token_is_not_a_credential_here()
    {
        await using Server server = await Server.StartAsync(fixture);

        using HttpClient signedIn = await fixture.SignInAsAdministratorAsync();
        string accessToken = signedIn.DefaultRequestHeaders.Authorization!.Parameter!;

        using HttpResponseMessage response = await server.PostAsync(accessToken, Initialize());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task an_invented_token_and_one_from_before_workspace_scoping_are_refused_alike()
    {
        await using Server server = await Server.StartAsync(fixture);

        UserId owner = await fixture.CreateUserAsync($"http-legacy-{Guid.NewGuid():N}@example.test");
        await fixture.GrantAsync(owner, Role.Contributor);
        string legacy = await fixture.PlantLegacyMachineTokenAsync(owner, "from the old release");

        using HttpResponseMessage invented = await server.PostAsync("not-a-token-anybody-issued", Initialize());
        using HttpResponseMessage unscoped = await server.PostAsync(legacy, Initialize());

        Assert.Equal(HttpStatusCode.Unauthorized, invented.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unscoped.StatusCode);
    }

    [Fact]
    public async Task a_machine_token_works_at_mcp_and_nowhere_else()
    {
        await using Server server = await Server.StartAsync(fixture);

        UserId owner = await fixture.CreateUserAsync($"http-plugin-{Guid.NewGuid():N}@example.test");
        await fixture.GrantAsync(owner, Role.Contributor);
        string token = await fixture.IssueMachineTokenAsync(owner, "a laptop");

        string answer = await server.CallAsync(
            token, UseCaseCatalog.ListProjects.Name, new { workspaceId = fixture.Workspace.Value });

        Assert.DoesNotContain("Refused", answer, StringComparison.Ordinal);

        using HttpResponseMessage root = await server.PostAsync(token, Initialize(), path: "/");
        Assert.Equal(HttpStatusCode.NotFound, root.StatusCode);
    }

    [Fact]
    public async Task a_token_minted_in_one_workspace_is_refused_in_another_its_owner_belongs_to()
    {
        await using Server server = await Server.StartAsync(fixture);

        UserId owner = await fixture.CreateUserAsync($"http-two-workspaces-{Guid.NewGuid():N}@example.test");
        await fixture.GrantAsync(owner, Role.Contributor);

        ProjectScope elsewhere = await fixture.CreateSeparateWorkspaceAsync(owner);
        await fixture.GrantInAsync(elsewhere.WorkspaceId, owner, Role.Contributor);

        string token = await fixture.IssueMachineTokenAsync(owner, "a laptop");

        string here = await server.CallAsync(
            token, UseCaseCatalog.ListProjects.Name, new { workspaceId = fixture.Workspace.Value });
        string there = await server.CallAsync(
            token, UseCaseCatalog.ListProjects.Name, new { workspaceId = elsewhere.WorkspaceId.Value });

        Assert.DoesNotContain("Refused", here, StringComparison.Ordinal);
        Assert.Contains("Refused", there, StringComparison.Ordinal);
        Assert.Contains("credential", there, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task revoking_a_token_stops_it_on_the_next_request()
    {
        await using Server server = await Server.StartAsync(fixture);

        string email = $"http-revoked-{Guid.NewGuid():N}@example.test";
        UserId owner = await fixture.CreateUserAsync(email);
        await fixture.GrantAsync(owner, Role.Contributor);
        string token = await fixture.IssueMachineTokenAsync(owner, "a laptop that was lost");

        using (HttpResponseMessage before = await server.PostAsync(token, Initialize()))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        using HttpClient client = await fixture.SignInAsync(email, ApiFixture.AdministratorPassword);

        JsonElement listed = await Post(
            client, UseCaseCatalog.ListMachineTokens.Name, new { workspaceId = fixture.Workspace.Value });

        Guid tokenId = listed.GetProperty("tokens").EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == "a laptop that was lost")
            .GetProperty("id")
            .GetGuid();

        await Post(
            client,
            UseCaseCatalog.RevokeMachineToken.Name,
            new { workspaceId = fixture.Workspace.Value, tokenId });

        // The same running server: nothing about the token was cached between requests.
        using HttpResponseMessage after = await server.PostAsync(token, Initialize());
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    /// <summary>
    /// A server started with a token in its environment, as a stdio process would be, must not
    /// treat a request that brought none as that token's owner.
    /// </summary>
    [Fact]
    public async Task a_token_in_the_servers_environment_is_never_lent_to_a_request()
    {
        UserId owner = await fixture.CreateUserAsync($"http-env-{Guid.NewGuid():N}@example.test");
        await fixture.GrantAsync(owner, Role.Contributor);
        string token = await fixture.IssueMachineTokenAsync(owner, "left in the environment");

        await using Server server = await Server.StartAsync(fixture, ("DEVBUDDY_TOKEN", token));

        using HttpResponseMessage response = await server.PostAsync(bearer: null, Initialize());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task the_rate_limit_counts_per_person_so_one_loop_does_not_stop_everybody()
    {
        await using Server server = await Server.StartAsync(
            fixture,
            ("DEVBUDDY_RateLimiting__RequestPermitLimit", "3"),
            ("DEVBUDDY_RateLimiting__RequestWindowSeconds", "300"));

        UserId looping = await fixture.CreateUserAsync($"http-loop-{Guid.NewGuid():N}@example.test");
        await fixture.GrantAsync(looping, Role.Contributor);
        string loopingToken = await fixture.IssueMachineTokenAsync(looping, "a runaway loop");

        UserId working = await fixture.CreateUserAsync($"http-working-{Guid.NewGuid():N}@example.test");
        await fixture.GrantAsync(working, Role.Contributor);
        string workingToken = await fixture.IssueMachineTokenAsync(working, "somebody working");

        for (int attempt = 0; attempt < 3; attempt++)
        {
            using HttpResponseMessage allowed = await server.PostAsync(loopingToken, Initialize());
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using HttpResponseMessage limited = await server.PostAsync(loopingToken, Initialize());
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        using HttpResponseMessage unaffected = await server.PostAsync(workingToken, Initialize());
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);

        // A request with no working token is not counted against anybody: it is refused as
        // unauthenticated, not as over a limit it could otherwise exhaust for everyone.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage stranger = await server.PostAsync("not-a-token", Initialize());
            Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
        }
    }

    /// <summary>
    /// The whole path a person takes (ADR-0015): the <c>devbuddy</c> client registers a checkout
    /// against this server with a token typed once, and the header it later hands an assistant is
    /// the one that server accepts. A token from another workspace is refused at registration.
    /// </summary>
    [Fact]
    public async Task the_devbuddy_client_registers_a_checkout_and_its_header_works_on_the_transport()
    {
        await using Server server = await Server.StartAsync(fixture);

        UserId owner = await fixture.CreateUserAsync($"http-client-{Guid.NewGuid():N}@example.test");
        await fixture.GrantAsync(owner, Role.Contributor);
        string token = await fixture.IssueMachineTokenAsync(owner, "registered through the client");

        ProjectScope elsewhere = await fixture.CreateSeparateWorkspaceAsync(owner);
        await fixture.GrantInAsync(elsewhere.WorkspaceId, owner, Role.Contributor);
        string elsewhereToken = await fixture.IssueMachineTokenAsync(owner, "another workspace", elsewhere.WorkspaceId);

        DirectoryInfo scratch = Directory.CreateTempSubdirectory("devbuddy-client-e2e-");

        try
        {
            string checkout = Directory.CreateDirectory(Path.Combine(scratch.FullName, "api", ".git")).Parent!.FullName;
            string home = Path.Combine(scratch.FullName, "home");

            // The token from the other workspace first: checked against the server, refused, not stored.
            (int wrong, _, string wrongError) = await RunClientAsync(
                home, elsewhereToken,
                "register", checkout, "--server", server.Origin, "--workspace", fixture.Workspace.Value.ToString());

            Assert.Equal(1, wrong);
            Assert.Contains("not in this workspace", wrongError, StringComparison.Ordinal);

            (int registered, _, string registerError) = await RunClientAsync(
                home, token,
                "register", checkout, "--server", server.Origin, "--workspace", fixture.Workspace.Value.ToString());

            Assert.True(registered == 0, registerError);

            (int helped, string headers, string helpError) = await RunClientAsync(
                home, input: null,
                "mcp-headers", "--dir", Path.Combine(checkout, "src"), "--url", $"{server.Origin}/mcp");

            Assert.True(helped == 0, helpError);

            string bearer = JsonSerializer.Deserialize<JsonElement>(headers)
                .GetProperty("Authorization").GetString()!["Bearer ".Length..];

            string answer = await server.CallAsync(
                bearer, UseCaseCatalog.ListProjects.Name, new { workspaceId = fixture.Workspace.Value });

            Assert.DoesNotContain("Refused", answer, StringComparison.Ordinal);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Runs the built <c>devbuddy</c> client with its own home and the file credential store, so it
    /// touches nothing of the person running the tests. The token arrives on standard input.
    /// </summary>
    private static async Task<(int Exit, string Output, string Error)> RunClientAsync(
        string home, string? input, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(Server.BuiltAssembly("src/clients/DevBuddy.Client", "devbuddy.dll").FullName);

        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["DEVBUDDY_HOME"] = home;
        start.Environment["DEVBUDDY_CREDENTIAL_STORE"] = "file";

        using Process client = Process.Start(start)
            ?? throw new InvalidOperationException("The devbuddy client did not start.");

        if (input is not null)
        {
            await client.StandardInput.WriteLineAsync(input);
        }

        client.StandardInput.Close();

        Task<string> output = client.StandardOutput.ReadToEndAsync();
        Task<string> error = client.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(Patience);
        await client.WaitForExitAsync(timeout.Token);

        return (client.ExitCode, await output, await error);
    }

    private static object Initialize() => new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "initialize",
        @params = new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "devbuddy-http-test", version = "1.0.0" },
        },
    };

    private static async Task<JsonElement> Post(HttpClient client, string operation, object arguments)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/operations/{operation}", arguments);

        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{operation} answered {response.StatusCode}: {body}");

        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    /// <summary>
    /// One MCP server process in HTTP mode, on a free loopback port, against the fixture's
    /// database. A process rather than an in-memory host because the stdio tests launch one too,
    /// and because the configuration a deployment gives it arrives the same way: environment
    /// variables prefixed <c>DEVBUDDY_</c>.
    /// </summary>
    private sealed class Server : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly HttpClient _http;

        private Server(Process process, Uri address)
        {
            _process = process;
            _http = new HttpClient { BaseAddress = address, Timeout = Patience };
            Origin = address.GetLeftPart(UriPartial.Authority);
        }

        /// <summary>Scheme, host and port, as the <c>devbuddy</c> client registers a server.</summary>
        public string Origin { get; }

        public static async Task<Server> StartAsync(
            ApiFixture fixture, params (string Name, string Value)[] environment)
        {
            FileInfo assembly = BuiltAssembly("src/hosts/DevBuddy.McpServer", "DevBuddy.McpServer.dll");
            int port = FreePort();

            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = assembly.DirectoryName!,
            };

            start.ArgumentList.Add(assembly.FullName);
            start.ArgumentList.Add("--urls");
            start.ArgumentList.Add($"http://127.0.0.1:{port}");

            start.Environment["DEVBUDDY_ConnectionStrings__DevBuddy"] = fixture.ConnectionString;
            start.Environment["DEVBUDDY_Identity__SigningKey"] = ApiFixture.SigningKey;
            start.Environment["DEVBUDDY_Evidence__Provider"] = "FileSystem";
            start.Environment.Remove("DEVBUDDY_TOKEN");

            foreach ((string name, string value) in environment)
            {
                start.Environment[name] = value;
            }

            Process process = Process.Start(start)
                ?? throw new InvalidOperationException("The MCP server process did not start.");

            // Drained so a chatty server cannot fill a pipe and stall.
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var server = new Server(process, new Uri($"http://127.0.0.1:{port}"));
            await server.WaitUntilListeningAsync();
            return server;
        }

        public async Task<HttpResponseMessage> PostAsync(string? bearer, object body, string path = "/mcp")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body),
            };

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            if (bearer is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }

            return await _http.SendAsync(request);
        }

        /// <summary>Initializes, then makes one tool call, and returns the JSON-RPC answer.</summary>
        public async Task<string> CallAsync(string bearer, string tool, object arguments)
        {
            using (HttpResponseMessage initialized = await PostAsync(bearer, Initialize()))
            {
                Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);
            }

            using HttpResponseMessage response = await PostAsync(bearer, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new { name = tool, arguments },
            });

            string body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"tools/call answered {response.StatusCode}: {body}");

            return JsonPayload(body);
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }

            _process.Dispose();
        }

        private async Task WaitUntilListeningAsync()
        {
            using var timeout = new CancellationTokenSource(Patience);

            while (!timeout.IsCancellationRequested)
            {
                if (_process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"The MCP server exited with {_process.ExitCode} before it listened.");
                }

                try
                {
                    using HttpResponseMessage probe = await PostAsync(bearer: null, Initialize());
                    return;
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250));
                }
            }

            throw new InvalidOperationException("The MCP server did not start listening in time.");
        }

        /// <summary>The body as JSON, or the last JSON event of a server-sent event stream.</summary>
        private static string JsonPayload(string body)
        {
            if (body.TrimStart().StartsWith('{'))
            {
                return body;
            }

            var payload = new StringBuilder();

            foreach (string line in body.Split('\n'))
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    payload.Clear().Append(line["data:".Length..].Trim());
                }
            }

            return payload.ToString();
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        /// <summary>A project's build output, in the configuration and framework the tests run in.</summary>
        public static FileInfo BuiltAssembly(string project, string fileName)
        {
            var here = new DirectoryInfo(AppContext.BaseDirectory);
            string framework = here.Name;
            string configuration = here.Parent?.Name ?? "Release";

            var directory = here;

            while (directory is not null && !directory.EnumerateFiles("DevBuddy.slnx").Any())
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);

            var assembly = new FileInfo(Path.Combine(
                [directory.FullName, .. project.Split('/'), "bin", configuration, framework, fileName]));

            Assert.True(assembly.Exists, $"{assembly.FullName} has not been built.");
            return assembly;
        }
    }
}
