using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace DevBuddy.Client.Tests;

/// <summary>
/// The stdio bridge Cowork starts, because it will not run a header helper: every message goes to
/// the checkout's own server with the checkout's token, and every answer comes back one line each.
/// </summary>
public sealed class McpBridgeTests : IDisposable
{
    private static readonly Guid Project = Guid.Parse("57b6c483-9bc2-48ea-9fe3-1e0cada0995b");

    private readonly ClientHarness _client = new();

    [Fact]
    public async Task a_message_goes_to_the_registered_servers_mcp_endpoint_with_the_token()
    {
        string checkout = Registered();

        Assert.Equal(Commands.Done, await BridgeAsync(checkout, """{"jsonrpc":"2.0","id":7,"method":"tools/list"}"""));

        Uri sent = Assert.Single(_client.Remote.Requests);
        Assert.Equal(new Uri($"{ClientHarness.Server}/mcp"), sent);
        Assert.Equal("""{"jsonrpc":"2.0","id":7,"method":"tools/list"}""", Assert.Single(_client.Remote.Bodies));
        Assert.Single(Lines());
    }

    [Fact]
    public async Task the_answer_to_initialize_names_the_workspace_and_the_default_project()
    {
        string checkout = Registered(Project);

        await BridgeAsync(
            checkout,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"cowork","version":"1"}}}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        JsonNode initialized = JsonNode.Parse(Lines()[0])!;
        string instructions = initialized["result"]!["instructions"]!.GetValue<string>();

        Assert.Contains(ClientHarness.Workspace.ToString("D"), instructions, StringComparison.Ordinal);
        Assert.Contains(Project.ToString("D"), instructions, StringComparison.Ordinal);
        Assert.Equal("2025-06-18", initialized["result"]!["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task requests_after_initialize_carry_the_negotiated_protocol_version()
    {
        string checkout = Registered();
        _client.Remote.InitializeDelay = TimeSpan.FromMilliseconds(300);

        await BridgeAsync(
            checkout,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        Assert.Equal([null, "2025-06-18"], _client.Remote.ProtocolVersions);
    }

    [Fact]
    public async Task a_notification_is_posted_and_nothing_is_written_back()
    {
        string checkout = Registered();

        await BridgeAsync(checkout, """{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.Single(_client.Remote.Bodies);
        Assert.Empty(Lines());
    }

    [Fact]
    public async Task every_event_in_a_stream_is_written_as_its_own_line_in_order()
    {
        string checkout = Registered();
        _client.Remote.Answer = _ => Stream(
            """{"jsonrpc":"2.0","method":"notifications/progress","params":{"progress":1}}""",
            """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"done"}]}}""");

        await BridgeAsync(checkout, """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"analyze_project"}}""");

        string[] lines = Lines();
        Assert.Equal(2, lines.Length);
        Assert.Contains("notifications/progress", lines[0], StringComparison.Ordinal);
        Assert.Contains("\"id\":3", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_json_answer_spread_over_lines_is_written_as_one()
    {
        string checkout = Registered();
        _client.Remote.Answer = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\n  \"jsonrpc\": \"2.0\",\n  \"id\": 4,\n  \"result\": {}\n}", Encoding.UTF8, "application/json"),
        };

        await BridgeAsync(checkout, """{"jsonrpc":"2.0","id":4,"method":"ping"}""");

        Assert.Equal("""{"jsonrpc":"2.0","id":4,"result":{}}""", Assert.Single(Lines()));
    }

    /// <summary>The console's code page must not decide the encoding: Thai is what the records hold.</summary>
    [Fact]
    public async Task thai_comes_back_as_utf8_with_no_byte_order_mark()
    {
        string checkout = Registered();
        _client.Remote.Answer = _ => Stream("""{"jsonrpc":"2.0","id":5,"result":{"content":[{"type":"text","text":"การตัดสินใจ"}]}}""");

        await BridgeAsync(checkout, """{"jsonrpc":"2.0","id":5,"method":"tools/call"}""");

        byte[] written = _client.Output.ToArray();
        Assert.NotEqual(0xEF, written[0]);
        Assert.Equal(
            "การตัดสินใจ",
            JsonNode.Parse(Lines()[0])!["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task a_refused_token_is_answered_with_an_error_the_assistant_can_show()
    {
        string checkout = Registered();
        _client.Remote.Accepted.Clear();

        await BridgeAsync(checkout, """{"jsonrpc":"2.0","id":"a","method":"tools/list"}""");

        JsonNode answer = JsonNode.Parse(Assert.Single(Lines()))!;
        Assert.Equal("a", answer["id"]!.GetValue<string>());
        Assert.Contains("refused the token", answer["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task with_no_token_stored_nothing_is_sent_and_the_request_is_answered_with_why()
    {
        string checkout = Registered();
        _client.Store.Delete(CredentialStores.KeyFor(ClientHarness.Server, ClientHarness.Workspace));

        await BridgeAsync(checkout, """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        Assert.Empty(_client.Remote.Requests);
        Assert.Contains("devbuddy token set", Assert.Single(Lines()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task an_unregistered_folder_sends_nothing()
    {
        string plain = Directory.CreateDirectory(Path.Combine(_client.Root, "elsewhere")).FullName;

        Assert.Equal(Commands.NotRegistered, await BridgeAsync(plain, """{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""));

        Assert.Empty(_client.Remote.Requests);
        Assert.Empty(Lines());
    }

    [Fact]
    public async Task something_that_is_not_json_is_answered_with_a_parse_error_and_not_sent()
    {
        string checkout = Registered();

        await BridgeAsync(checkout, "not json");

        Assert.Empty(_client.Remote.Requests);
        Assert.Equal(-32700, JsonNode.Parse(Assert.Single(Lines()))!["error"]!["code"]!.GetValue<int>());
    }

    /// <summary>An assistant that does not know a variable passes it on as written.</summary>
    [Fact]
    public async Task a_dir_left_as_an_unexpanded_variable_falls_back_to_the_working_folder()
    {
        string checkout = Registered();
        _client.WorkingDirectory = checkout;

        Assert.Equal(Commands.Done, await RunAsync(["mcp-bridge", "--dir", "${CLAUDE_PROJECT_DIR}"], """{"jsonrpc":"2.0","id":1,"method":"ping"}"""));

        Assert.Single(_client.Remote.Requests);
    }

    [Fact]
    public async Task the_log_names_methods_and_statuses_and_never_the_token()
    {
        string checkout = Registered();
        string log = Path.Combine(_client.Root, "bridge.log");

        await RunAsync(["mcp-bridge", "--dir", checkout, "--log", log], """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        string written = File.ReadAllText(log);
        Assert.Contains("tools/list -> 200", written, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientHarness.GoodToken, written, StringComparison.Ordinal);
    }

    public void Dispose() => _client.Dispose();

    private string Registered(Guid? project = null)
    {
        string checkout = Directory.CreateDirectory(Path.Combine(_client.Root, "requirements")).FullName;
        var registry = Registry.Load(_client.Home);
        registry.Put(new Checkout(checkout, ClientHarness.Server, ClientHarness.Workspace, project));
        registry.Save();
        _client.Store.Write(CredentialStores.KeyFor(ClientHarness.Server, ClientHarness.Workspace), ClientHarness.GoodToken);
        _client.Remote.Accepted.Add(ClientHarness.GoodToken);
        return checkout;
    }

    private Task<int> BridgeAsync(string folder, params string[] messages) =>
        RunAsync(["mcp-bridge", "--dir", folder], messages);

    private Task<int> RunAsync(string[] args, params string[] messages)
    {
        _client.Input = new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\n", messages) + "\n"));
        return _client.RunAsync(args);
    }

    private string[] Lines() =>
        Encoding.UTF8.GetString(_client.Output.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static HttpResponseMessage Stream(params string[] events) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                string.Concat(events.Select(data => $"event: message\ndata: {data}\n\n")), Encoding.UTF8, "text/event-stream"),
        };
}
