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

    /// <summary>
    /// With nothing registered the bridge still serves, and every call says why: a server that
    /// exits shows in Cowork only as a connector that failed.
    /// </summary>
    [Fact]
    public async Task with_nothing_registered_nothing_is_sent_and_every_call_says_why()
    {
        string plain = Directory.CreateDirectory(Path.Combine(_client.Root, "elsewhere")).FullName;

        Assert.Equal(Commands.Done, await BridgeAsync(
            plain,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_projects"}}"""));

        Assert.Empty(_client.Remote.Requests);
        string[] lines = Lines();
        Assert.Equal("2025-06-18", JsonNode.Parse(lines[0])!["result"]!["protocolVersion"]!.GetValue<string>());
        JsonNode call = JsonNode.Parse(lines[1])!["result"]!;
        Assert.True(call["isError"]!.GetValue<bool>());
        Assert.Contains("devbuddy register", call["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
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

    /// <summary>
    /// Cowork did not pass the server's instructions on, so the assistant guessed a workspace and
    /// was refused. The schemas it sees ask for none.
    /// </summary>
    [Fact]
    public async Task the_tool_list_asks_for_no_workspace()
    {
        string checkout = Registered();
        _client.Remote.Answer = body => body.Contains("tools/list", StringComparison.Ordinal) ? Stream(ToolList) : null;

        await BridgeAsync(checkout, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        JsonArray tools = JsonNode.Parse(Lines()[0])!["result"]!["tools"]!.AsArray();
        JsonNode listProjects = tools.Single(tool => tool!["name"]!.GetValue<string>() == "list_projects")!["inputSchema"]!;
        JsonNode scope = tools.Single(tool => tool!["name"]!.GetValue<string>() == "list_records")!["inputSchema"]!["properties"]!["scope"]!;

        Assert.Null(listProjects["properties"]!["workspaceId"]);
        Assert.Empty(listProjects["required"]!.AsArray());
        Assert.Null(scope["properties"]!["workspaceId"]);
        Assert.Equal(["projectId"], scope["required"]!.AsArray().Select(entry => entry!.GetValue<string>()));
    }

    [Fact]
    public async Task a_call_is_sent_with_the_registered_workspace_where_each_tool_takes_it()
    {
        string checkout = Registered();
        _client.Remote.Answer = body => body.Contains("tools/list", StringComparison.Ordinal) ? Stream(ToolList) : null;

        await BridgeAsync(
            checkout,
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_projects"}}""",
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"list_records","arguments":{"scope":{"projectId":"57b6c483-9bc2-48ea-9fe3-1e0cada0995b"}}}}""");

        string workspace = ClientHarness.Workspace.ToString("D");
        JsonNode listProjects = Sent("list_projects");
        JsonNode listRecords = Sent("list_records");

        Assert.Equal(workspace, listProjects["params"]!["arguments"]!["workspaceId"]!.GetValue<string>());
        Assert.Equal(workspace, listRecords["params"]!["arguments"]!["scope"]!["workspaceId"]!.GetValue<string>());
        Assert.Equal(Project.ToString("D"), listRecords["params"]!["arguments"]!["scope"]!["projectId"]!.GetValue<string>());
    }

    /// <summary>What the assistant in Cowork did: it wrote a workspace of its own.</summary>
    [Fact]
    public async Task a_workspace_the_assistant_made_up_is_replaced_before_it_is_sent()
    {
        string checkout = Registered();

        await BridgeAsync(
            checkout,
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"search_knowledge","arguments":{"queryText":"x","scope":{"workspaceId":"3f2a9c1e-0000-0000-0000-000000000000","projectId":"57b6c483-9bc2-48ea-9fe3-1e0cada0995b"}}}}""");

        Assert.Equal(
            ClientHarness.Workspace.ToString("D"),
            Sent("search_knowledge")["params"]!["arguments"]!["scope"]!["workspaceId"]!.GetValue<string>());
    }

    /// <summary>
    /// Cowork starts the bridge in the system folder and says nothing about where the person
    /// works, so a person with one workspace registered needs no configuration.
    /// </summary>
    [Fact]
    public async Task outside_any_checkout_the_only_registered_workspace_is_used()
    {
        Registered(Project);
        string elsewhere = Directory.CreateDirectory(Path.Combine(_client.Root, "System32")).FullName;

        Assert.Equal(Commands.Done, await BridgeAsync(
            elsewhere, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}"""));

        Assert.Single(_client.Remote.Requests);
        Assert.Contains(Project.ToString("D"), Lines()[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Two customers' workspaces on one machine, and a task in Cowork, which names no folder: no
    /// tool reaches either until the person has said which one the task is for.
    /// </summary>
    [Fact]
    public async Task with_two_workspaces_no_tool_call_is_sent_until_one_is_chosen()
    {
        string elsewhere = TwoWorkspaces();

        Assert.Equal(Commands.Done, await BridgeAsync(
            elsewhere, """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_projects"}}"""));

        Assert.Empty(_client.Remote.Requests);
        JsonNode result = JsonNode.Parse(Assert.Single(Lines()))!["result"]!;
        string text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("Customer A", text, StringComparison.Ordinal);
        Assert.Contains("Customer B", text, StringComparison.Ordinal);
        Assert.Contains(McpBridge.ChooseTool, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task with_two_workspaces_the_tool_list_offers_the_choice_naming_both()
    {
        string elsewhere = TwoWorkspaces();
        _client.Remote.Answer = body => body.Contains("tools/list", StringComparison.Ordinal) ? Stream(ToolList) : null;

        await BridgeAsync(elsewhere, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        JsonNode choose = JsonNode.Parse(Lines()[0])!["result"]!["tools"]![0]!;
        Assert.Equal(McpBridge.ChooseTool, choose["name"]!.GetValue<string>());
        Assert.Contains("Customer A", choose["description"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("Customer B", choose["description"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task once_chosen_every_call_goes_to_that_workspace_with_its_own_token()
    {
        string elsewhere = TwoWorkspaces();

        await BridgeAsync(
            elsewhere,
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"use_workspace","arguments":{"workspace":"customer b"}}}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_projects"}}""");

        Assert.False(JsonNode.Parse(Lines()[0])!["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal(OtherWorkspace.ToString("D"), Sent("list_projects")["params"]!["arguments"]!["workspaceId"]!.GetValue<string>());
        Assert.Equal(OtherToken, Assert.Single(_client.Remote.Bearers));
    }

    /// <summary>What was read in one customer's workspace must not follow the task into another's.</summary>
    [Fact]
    public async Task a_task_that_has_chosen_cannot_choose_another_workspace()
    {
        string elsewhere = TwoWorkspaces();

        await BridgeAsync(
            elsewhere,
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"use_workspace","arguments":{"workspace":"Customer A"}}}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"use_workspace","arguments":{"workspace":"Customer B"}}}""",
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"list_projects"}}""");

        JsonNode second = JsonNode.Parse(Lines()[1])!["result"]!;
        Assert.True(second["isError"]!.GetValue<bool>());
        Assert.Contains("new task", second["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(
            ClientHarness.Workspace.ToString("D"),
            Sent("list_projects")["params"]!["arguments"]!["workspaceId"]!.GetValue<string>());
        Assert.Equal(ClientHarness.GoodToken, Assert.Single(_client.Remote.Bearers));
    }

    [Fact]
    public async Task a_workspace_can_be_chosen_by_its_identifier_and_not_by_a_name_it_does_not_have()
    {
        string elsewhere = TwoWorkspaces();

        await BridgeAsync(
            elsewhere,
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"use_workspace","arguments":{"workspace":"Customer C"}}}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"use_workspace","arguments":{"workspace":"0b7e1d7a-3c55-4c1e-9d59-1f0c2b8a6e21"}}}""");

        Assert.True(JsonNode.Parse(Lines()[0])!["result"]!["isError"]!.GetValue<bool>());
        Assert.False(JsonNode.Parse(Lines()[1])!["result"]!["isError"]!.GetValue<bool>());
    }

    /// <summary>Choosing a workspace this machine holds no token for would hold the task to one it cannot use.</summary>
    [Fact]
    public async Task a_workspace_with_no_token_here_is_not_chosen_and_another_still_can_be()
    {
        string elsewhere = TwoWorkspaces();
        _client.Store.Delete(CredentialStores.KeyFor(ClientHarness.Server, OtherWorkspace));

        await BridgeAsync(
            elsewhere,
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"use_workspace","arguments":{"workspace":"Customer B"}}}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"use_workspace","arguments":{"workspace":"Customer A"}}}""");

        JsonNode refused = JsonNode.Parse(Lines()[0])!["result"]!;
        Assert.True(refused["isError"]!.GetValue<bool>());
        Assert.Contains("devbuddy token set", refused["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.False(JsonNode.Parse(Lines()[1])!["result"]!["isError"]!.GetValue<bool>());
    }

    /// <summary>A folder that is registered still decides, as it does for Claude Code, and offers no choice.</summary>
    [Fact]
    public async Task with_two_workspaces_a_registered_folder_still_decides()
    {
        TwoWorkspaces();
        string customerB = Path.Combine(_client.Root, "customer-b");
        _client.Remote.Answer = body => body.Contains("tools/list", StringComparison.Ordinal) ? Stream(ToolList) : null;

        await BridgeAsync(
            customerB,
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_projects"}}""");

        Assert.DoesNotContain(McpBridge.ChooseTool, Lines()[0], StringComparison.Ordinal);
        Assert.Equal(OtherWorkspace.ToString("D"), Sent("list_projects")["params"]!["arguments"]!["workspaceId"]!.GetValue<string>());
    }

    [Fact]
    public async Task a_workspace_with_no_label_is_named_by_its_registered_folders()
    {
        Registered();
        var registry = Registry.Load(_client.Home);
        registry.Put(new Checkout(
            Directory.CreateDirectory(Path.Combine(_client.Root, "customer-b")).FullName, ClientHarness.Server, OtherWorkspace, null));
        registry.Save();
        string elsewhere = Directory.CreateDirectory(Path.Combine(_client.Root, "System32")).FullName;

        await BridgeAsync(elsewhere, """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_projects"}}""");

        string text = JsonNode.Parse(Assert.Single(Lines()))!["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("requirements", text, StringComparison.Ordinal);
        Assert.Contains("customer-b", text, StringComparison.Ordinal);
    }

    /// <summary>What a managed configuration passes: no folder, no registration, a token already stored.</summary>
    [Fact]
    public async Task a_server_and_workspace_given_need_no_registration()
    {
        _client.Store.Write(CredentialStores.KeyFor(ClientHarness.Server, ClientHarness.Workspace), ClientHarness.GoodToken);
        _client.Remote.Accepted.Add(ClientHarness.GoodToken);

        Assert.Equal(Commands.Done, await RunAsync(
            ["mcp-bridge", "--server", ClientHarness.Server + "/", "--workspace", ClientHarness.Workspace.ToString()],
            """{"jsonrpc":"2.0","id":1,"method":"ping"}"""));

        Assert.Equal(new Uri($"{ClientHarness.Server}/mcp"), Assert.Single(_client.Remote.Requests));
    }

    /// <summary>The token is kept per server, so a configuration naming another server finds none to send.</summary>
    [Fact]
    public async Task a_server_given_that_holds_no_token_is_sent_nothing()
    {
        Registered();

        await RunAsync(
            ["mcp-bridge", "--server", "https://elsewhere.example.test", "--workspace", ClientHarness.Workspace.ToString()],
            """{"jsonrpc":"2.0","id":1,"method":"ping"}""");

        Assert.Empty(_client.Remote.Requests);
        Assert.Contains("devbuddy token set", Assert.Single(Lines()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--server", "http://192.168.1.160:5010")]
    [InlineData("--server", "https://devbuddy.example.test:5010", "--workspace", "not-an-id")]
    [InlineData("--workspace", "5cd6f516-5b45-45ed-a5c9-6cdbb60df2dc")]
    public async Task a_server_or_workspace_that_cannot_be_used_is_refused_before_anything_is_sent(params string[] options)
    {
        Registered();
        string[] args = options.Length == 2 && options[0] == "--server"
            ? ["mcp-bridge", .. options, "--workspace", ClientHarness.Workspace.ToString()]
            : ["mcp-bridge", .. options];

        Assert.Equal(Commands.Done, await RunAsync(
            args, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_projects"}}"""));

        Assert.Empty(_client.Remote.Requests);
        Assert.True(JsonNode.Parse(Assert.Single(Lines()))!["result"]!["isError"]!.GetValue<bool>());
    }

    /// <summary>A plugin setting nobody filled in reaches the bridge as written, and counts as absent.</summary>
    [Fact]
    public async Task settings_left_unexpanded_fall_back_to_the_registration()
    {
        string checkout = Registered();
        _client.WorkingDirectory = checkout;

        Assert.Equal(Commands.Done, await RunAsync(
            ["mcp-bridge", "--server", "${user_config.server}", "--workspace", "${user_config.workspace}"],
            """{"jsonrpc":"2.0","id":1,"method":"ping"}"""));

        Assert.Single(_client.Remote.Requests);
    }

    public void Dispose() => _client.Dispose();

    /// <summary>Two tools in the shapes the server gives them: the workspace alone, or in a scope.</summary>
    private const string ToolList =
        """{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"list_projects","inputSchema":{"type":"object","properties":{"workspaceId":{"type":"string","format":"uuid"}},"required":["workspaceId"]}},{"name":"list_records","inputSchema":{"type":"object","properties":{"scope":{"type":"object","properties":{"workspaceId":{"type":"string","format":"uuid"},"projectId":{"type":"string","format":"uuid"}},"required":["workspaceId","projectId"]}},"required":["scope"]}}]}}""";

    private JsonNode Sent(string tool) =>
        _client.Remote.Bodies
            .Select(body => JsonNode.Parse(body)!)
            .Single(message => message["params"]?["name"]?.GetValue<string>() == tool);

    private static readonly Guid OtherWorkspace = Guid.Parse("0b7e1d7a-3c55-4c1e-9d59-1f0c2b8a6e21");

    private static readonly string OtherToken = new('B', 43);

    /// <summary>Customer A's workspace in <c>requirements</c>, customer B's in <c>customer-b</c>, and a folder in neither.</summary>
    private string TwoWorkspaces()
    {
        Registered(label: "Customer A");
        var registry = Registry.Load(_client.Home);
        registry.Put(new Checkout(
            Directory.CreateDirectory(Path.Combine(_client.Root, "customer-b")).FullName,
            ClientHarness.Server, OtherWorkspace, null, "Customer B"));
        registry.Save();
        _client.Store.Write(CredentialStores.KeyFor(ClientHarness.Server, OtherWorkspace), OtherToken);
        _client.Remote.Accepted.Add(OtherToken);
        return Directory.CreateDirectory(Path.Combine(_client.Root, "System32")).FullName;
    }

    private string Registered(Guid? project = null, string? label = null)
    {
        string checkout = Directory.CreateDirectory(Path.Combine(_client.Root, "requirements")).FullName;
        var registry = Registry.Load(_client.Home);
        registry.Put(new Checkout(checkout, ClientHarness.Server, ClientHarness.Workspace, project, label));
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
