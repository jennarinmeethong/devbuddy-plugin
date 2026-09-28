using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevBuddy.Client;

/// <summary>
/// Where a bridge may send: the workspaces it may choose between, each with the name shown to the
/// person, or, when there is none it can use, why not.
/// </summary>
internal sealed record BridgeTargets(IReadOnlyList<Checkout> Choices, string? Problem)
{
    public static BridgeTargets One(Checkout target) => new([target], null);

    public static BridgeTargets None(string problem) => new([], problem);
}

/// <summary>
/// MCP over stdio on one side and DevBuddy's HTTP transport on the other, for an assistant that
/// starts a local server but will not run a header helper: Cowork reads <c>url</c>, <c>headers</c>
/// and <c>oauth</c> from a plugin's <c>.mcp.json</c> and nothing else.
/// <para>
/// Each line on standard input is one JSON-RPC message, posted to the server with the token
/// stored for that server and workspace, read from the store for every request so a replaced
/// token is picked up. The token is keyed by the server, so it goes nowhere else. Every message
/// in the answer, JSON or an event stream, is written back as one line. A request the server or
/// the network refuses is answered with a JSON-RPC error saying why, so the assistant can tell the
/// person.
/// </para>
/// <para>
/// With more than one workspace to choose from, no tool reaches DevBuddy until the person has
/// chosen one through <c>use_workspace</c>, and the choice holds until the task ends: Cowork starts
/// a bridge per task and tells it nothing about the task's folder, so this is how one task reads
/// one customer's knowledge and no other's (<c>info.md</c>, 2026-09-29). With none, the bridge still
/// starts and answers every call with the reason, because a server that exits shows in Cowork only
/// as a connector that failed.
/// </para>
/// </summary>
internal sealed class McpBridge(ClientContext context, BridgeTargets targets, BridgeLog log) : IDisposable
{
    public const string ChooseTool = "use_workspace";

    /// <summary>Longer than any operation should take, so a slow analysis is not cut off.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly Lock _state = new();
    private readonly HashSet<string> _workspaceArgument = new(StringComparer.Ordinal);
    private readonly HashSet<string> _workspaceInScope = new(StringComparer.Ordinal);
    private string? _protocolVersion;
    private Checkout? _chosen = targets.Choices.Count == 1 ? targets.Choices[0] : null;

    private bool MustChoose => targets.Choices.Count > 1;

    public void Dispose() => _writing.Dispose();

    public async Task RunAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        using var http = new HttpClient(context.Http, disposeHandler: false) { Timeout = RequestTimeout };
        using var reader = new StreamReader(input, new UTF8Encoding(false));
        await using var writer = new StreamWriter(output, new UTF8Encoding(false)) { NewLine = "\n" };

        var inFlight = new List<Task>();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0 || line.All(char.IsWhiteSpace))
            {
                continue;
            }

            Task handled = HandleAsync(http, line, writer, cancellationToken);

            // initialize settles the protocol version every later request carries, and a client
            // sends nothing else before its answer. Everything after it is concurrent, as the
            // protocol allows: one slow analysis must not hold up a search.
            if (IsInitialize(line))
            {
                await handled;
            }
            else
            {
                inFlight.Add(handled);
                inFlight.RemoveAll(task => task.IsCompleted);
            }
        }

        await Task.WhenAll(inFlight);
    }

    private static bool IsInitialize(string line)
    {
        try
        {
            return JsonNode.Parse(line)?["method"]?.GetValue<string>() == "initialize";
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task HandleAsync(HttpClient http, string line, StreamWriter writer, CancellationToken cancellationToken)
    {
        JsonNode? message;

        try
        {
            message = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            await WriteAsync(writer, Error(null, -32700, "DevBuddy: that was not a JSON-RPC message."), cancellationToken);
            return;
        }

        JsonObject? incoming = message as JsonObject;
        JsonNode? id = incoming is not null && incoming.TryGetPropertyValue("id", out JsonNode? given) ? given : null;
        string method = incoming?["method"] is JsonValue name && name.TryGetValue(out string? text) ? text : "(response)";
        string? tool = method == "tools/call" && incoming?["params"]?["name"] is JsonValue called
            && called.TryGetValue(out string? calledName) ? calledName : null;

        if (targets.Problem is { } problem)
        {
            await AnswerLocallyAsync(writer, id, method, incoming, problem, cancellationToken);
            return;
        }

        if (tool == ChooseTool)
        {
            await ChooseAsync(writer, id, incoming, cancellationToken);
            return;
        }

        Checkout? chosen;

        lock (_state)
        {
            chosen = _chosen;
        }

        if (tool is not null && chosen is null)
        {
            log.Write($"tools/call {tool} held: no workspace chosen");
            await WriteAsync(writer, ToolResult(id, ChooseFirst(), isError: true), cancellationToken);
            return;
        }

        // What is not a tool call reads nothing in a workspace, so before the choice it goes to
        // the first; a tool call goes only to the one chosen.
        Checkout target = chosen ?? targets.Choices[0];
        string body = tool is not null && incoming is not null ? FillWorkspace(incoming, target) : line;

        await ForwardAsync(http, target, body, id, method, writer, cancellationToken);
    }

    private async Task ForwardAsync(
        HttpClient http, Checkout target, string body, JsonNode? id, string method, StreamWriter writer,
        CancellationToken cancellationToken)
    {
        string? token;

        try
        {
            token = context.Store.Read(CredentialStores.KeyFor(target.Server, target.Workspace));
        }
        catch (InvalidOperationException exception)
        {
            await RefuseAsync(writer, id, method, $"DevBuddy: the credential store could not be read. {exception.Message}", cancellationToken);
            return;
        }

        if (token is null)
        {
            await RefuseAsync(
                writer, id, method,
                $"DevBuddy: no token is stored for {target.Server}, workspace {target.Workspace:D}. Run `devbuddy token set`.",
                cancellationToken);
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ServerOrigin.McpEndpoint(target.Server))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            if (_protocolVersion is { } version)
            {
                request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", version);
            }

            using HttpResponseMessage response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            log.Write($"{method} -> {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                await RefuseAsync(writer, id, method, Refusal(target, response.StatusCode), cancellationToken);
                return;
            }

            await foreach (string answer in AnswersAsync(response, cancellationToken))
            {
                string relayed = method switch
                {
                    "initialize" => Introduce(answer),
                    "tools/list" => HideWorkspace(answer),
                    _ => answer,
                };

                await WriteAsync(writer, relayed, cancellationToken);
            }
        }
        catch (HttpRequestException exception) when (exception.InnerException is AuthenticationException)
        {
            await RefuseAsync(
                writer, id, method,
                new CheckResult(CheckOutcome.CertificateNotTrusted, exception.InnerException.Message).Explain(target.Server),
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            await RefuseAsync(
                writer, id, method,
                new CheckResult(CheckOutcome.Unreachable, exception.Message).Explain(target.Server),
                cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RefuseAsync(
                writer, id, method,
                $"DevBuddy: {target.Server} did not answer within {RequestTimeout.TotalMinutes:0} minutes.",
                cancellationToken);
        }
    }

    /// <summary>
    /// The person's choice of workspace for this task, made once. A second choice of the same one
    /// is harmless; of another, it is refused, because the task has already read the first.
    /// </summary>
    private async Task ChooseAsync(StreamWriter writer, JsonNode? id, JsonObject? call, CancellationToken cancellationToken)
    {
        string wanted = call?["params"]?["arguments"]?["workspace"] is JsonValue value && value.TryGetValue(out string? text)
            ? text.Trim()
            : string.Empty;

        Checkout? match = targets.Choices.FirstOrDefault(choice =>
            string.Equals(choice.Label, wanted, StringComparison.OrdinalIgnoreCase)
            || (Guid.TryParse(wanted, out Guid workspace) && choice.Workspace == workspace));

        if (match is null)
        {
            await WriteAsync(
                writer,
                ToolResult(id, $"DevBuddy: '{wanted}' is none of the workspaces on this machine: {Names()}. Ask the person which one.", isError: true),
                cancellationToken);
            return;
        }

        // A workspace with no token here would hold the task to something it cannot use.
        string? missing = null;

        try
        {
            if (context.Store.Read(CredentialStores.KeyFor(match.Server, match.Workspace)) is null)
            {
                missing = $"DevBuddy: no token is stored on this machine for {Name(match)}, so it was not chosen. "
                    + "The person runs `devbuddy token set` for it, or chooses another.";
            }
        }
        catch (InvalidOperationException exception)
        {
            missing = $"DevBuddy: the credential store could not be read, so {Name(match)} was not chosen. {exception.Message}";
        }

        if (missing is not null)
        {
            log.Write($"{ChooseTool} refused: no token for {match.Workspace:D}");
            await WriteAsync(writer, ToolResult(id, missing, isError: true), cancellationToken);
            return;
        }

        Checkout? already;

        lock (_state)
        {
            already = _chosen;
            _chosen ??= match;
        }

        if (already is not null && already.Workspace != match.Workspace)
        {
            log.Write($"{ChooseTool} refused: this task already uses {already.Workspace:D}");
            await WriteAsync(
                writer,
                ToolResult(
                    id,
                    $"DevBuddy: this task already uses {Name(already)}, and keeps it until it ends, so what it read there "
                    + $"cannot reach {Name(match)}. Start a new task for {Name(match)}.",
                    isError: true),
                cancellationToken);
            return;
        }

        log.Write($"{ChooseTool}: {match.Workspace:D}");

        string project = match.Project is { } defaultProject
            ? $" Its default project is {defaultProject:D}."
            : " Call list_projects to see its projects.";

        await WriteAsync(
            writer,
            ToolResult(
                id,
                $"This task now uses {Name(match)}. Every DevBuddy call in it goes there, and to no other workspace; "
                + $"to use another, the person starts a new task.{project}",
                isError: false),
            cancellationToken);
    }

    /// <summary>
    /// Everything answered here, nothing sent: the bridge has no workspace it can use. Every tool
    /// call gets the reason as its result, which the assistant shows; an exit would not be shown.
    /// </summary>
    private async Task AnswerLocallyAsync(
        StreamWriter writer, JsonNode? id, string method, JsonObject? message, string problem, CancellationToken cancellationToken)
    {
        log.Write($"{method} answered here: {problem}");

        string? answer = method switch
        {
            "initialize" => Result(id, new JsonObject
            {
                ["protocolVersion"] = message?["params"]?["protocolVersion"]?.DeepClone() ?? "2025-06-18",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "devbuddy", ["version"] = "bridge" },
                ["instructions"] = problem,
            }),
            "tools/list" => Result(id, new JsonObject { ["tools"] = new JsonArray(ChooseToolDefinition(problem)) }),
            "tools/call" => ToolResult(id, problem, isError: true),
            _ when id is not null => Result(id, new JsonObject()),
            _ => null,
        };

        if (answer is not null)
        {
            await WriteAsync(writer, answer, cancellationToken);
        }
    }

    /// <summary>
    /// The messages in an answer: one JSON body, or each <c>data</c> event of a stream as it
    /// arrives, so progress reaches the assistant before the result. A 202 carries none.
    /// </summary>
    private static async IAsyncEnumerable<string> AnswersAsync(
        HttpResponseMessage response,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? mediaType = response.Content.Headers.ContentType?.MediaType;

        if (mediaType == "text/event-stream")
        {
            using var stream = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken), Encoding.UTF8);
            var data = new StringBuilder();

            while (await stream.ReadLineAsync(cancellationToken) is { } line)
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    data.Append(data.Length > 0 ? "\n" : string.Empty).Append(line.AsSpan(5).TrimStart());
                }
                else if (line.Length == 0 && data.Length > 0)
                {
                    yield return OneLine(data.ToString());
                    data.Clear();
                }
            }

            if (data.Length > 0)
            {
                yield return OneLine(data.ToString());
            }

            yield break;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(body))
        {
            yield return OneLine(body);
        }
    }

    /// <summary>
    /// The answer to <c>initialize</c>, with the workspace, or the choice to be made, added to the
    /// server's instructions. An assistant inside a sandbox cannot run <c>devbuddy show</c>.
    /// </summary>
    private string Introduce(string answer)
    {
        if (JsonNode.Parse(answer) is not JsonObject message || message["result"] is not JsonObject result)
        {
            return answer;
        }

        _protocolVersion = result["protocolVersion"]?.GetValue<string>() ?? _protocolVersion;

        string introduction;

        if (MustChoose)
        {
            introduction = ChooseFirst();
        }
        else
        {
            Checkout only = targets.Choices[0];
            string project = only.Project is { } id
                ? $"The default project is {id:D}."
                : "No default project is registered; call list_projects to find one.";
            introduction =
                $"This DevBuddy connection works in workspace {only.Workspace:D} and no other, and fills "
                + $"it in on every call, so no tool asks for it. {project} It is a registration, not DevBuddy content.";
        }

        string? existing = result["instructions"]?.GetValue<string>();
        result["instructions"] = string.IsNullOrEmpty(existing) ? introduction : $"{existing}\n\n{introduction}";

        return message.ToJsonString();
    }

    /// <summary>
    /// The tool list with the workspace taken out of every schema, and, when there is a choice to
    /// make, the tool that makes it. The token works in one workspace, so the bridge supplies it;
    /// an assistant asked for it would have to guess, and in Cowork one did.
    /// </summary>
    private string HideWorkspace(string answer)
    {
        if (JsonNode.Parse(answer)?["result"]?["tools"] is not JsonArray tools)
        {
            return answer;
        }

        foreach (JsonObject tool in tools.OfType<JsonObject>())
        {
            string? name = tool["name"]?.GetValue<string>();

            if (name is null || tool["inputSchema"] is not JsonObject schema)
            {
                continue;
            }

            if (Remove(schema, "workspaceId"))
            {
                lock (_state)
                {
                    _workspaceArgument.Add(name);
                }
            }

            if (schema["properties"]?["scope"] is JsonObject scope && Remove(scope, "workspaceId"))
            {
                lock (_state)
                {
                    _workspaceInScope.Add(name);
                }
            }
        }

        if (MustChoose)
        {
            tools.Insert(0, ChooseToolDefinition(ChooseFirst()));
        }

        return tools.Root.ToJsonString();
    }

    private JsonObject ChooseToolDefinition(string description) => new()
    {
        ["name"] = ChooseTool,
        ["description"] = description,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["workspace"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = targets.Choices.Count > 0
                        ? $"The name or identifier of one of: {Names()}."
                        : "There is nothing to choose from on this machine.",
                },
            },
            ["required"] = new JsonArray("workspace"),
        },
    };

    private string ChooseFirst() =>
        $"This machine has {targets.Choices.Count} DevBuddy workspaces: {Names()}. Before any other DevBuddy tool, "
        + $"ask the person which one this task is for, and call {ChooseTool} with it. Do not choose for them. The "
        + "choice holds until the task ends, so one task reads one workspace; another needs a new task.";

    private string Names() => string.Join("; ", targets.Choices.Select(Name));

    private static string Name(Checkout choice) => $"{choice.Label} ({choice.Workspace:D})";

    private static bool Remove(JsonObject schema, string property)
    {
        if (schema["properties"] is not JsonObject properties || !properties.Remove(property))
        {
            return false;
        }

        if (schema["required"] is JsonArray required
            && required.FirstOrDefault(entry => entry?.GetValue<string>() == property) is { } entry)
        {
            required.Remove(entry);
        }

        return true;
    }

    /// <summary>
    /// A tool call with the chosen workspace put where the tool takes it, replacing whatever the
    /// assistant wrote there. That grants nothing: the token is refused in any other workspace.
    /// </summary>
    private string FillWorkspace(JsonObject call, Checkout target)
    {
        if (call["params"] is not JsonObject parameters || parameters["name"]?.GetValue<string>() is not { } name)
        {
            return call.ToJsonString();
        }

        if (parameters["arguments"] is not JsonObject arguments)
        {
            arguments = [];
            parameters["arguments"] = arguments;
        }

        bool inScope;
        bool asArgument;

        lock (_state)
        {
            inScope = _workspaceInScope.Contains(name);
            asArgument = _workspaceArgument.Contains(name);
        }

        string workspace = target.Workspace.ToString("D");

        // Before a tool list has been seen, the shape of the call is the only guide.
        if (inScope || (!asArgument && arguments["scope"] is JsonObject))
        {
            if (arguments["scope"] is not JsonObject scope)
            {
                scope = [];
                arguments["scope"] = scope;
            }

            scope["workspaceId"] = workspace;
        }
        else
        {
            arguments["workspaceId"] = workspace;
        }

        return call.ToJsonString();
    }

    private static string Refusal(Checkout target, HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => $"DevBuddy: {new CheckResult(CheckOutcome.TokenRefused, string.Empty).Explain(target.Server)}",
        HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed =>
            $"DevBuddy: {new CheckResult(CheckOutcome.NoMcpEndpoint, string.Empty).Explain(target.Server)}",
        HttpStatusCode.TooManyRequests => $"DevBuddy: {target.Server} is limiting requests. Wait a minute and try again.",
        _ => $"DevBuddy: {target.Server} answered HTTP {(int)status}.",
    };

    /// <summary>
    /// A request is answered with an error, so the assistant has something to show; a
    /// notification expects no answer, and gets none.
    /// </summary>
    private async Task RefuseAsync(StreamWriter writer, JsonNode? id, string method, string reason, CancellationToken cancellationToken)
    {
        log.Write($"{method} refused: {reason}");

        if (id is not null)
        {
            await WriteAsync(writer, Error(id, -32000, reason), cancellationToken);
        }
    }

    private static string Error(JsonNode? id, int code, string text) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = text },
        }.ToJsonString();

    private static string Result(JsonNode? id, JsonObject result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result }.ToJsonString();

    /// <summary>A tool's answer the assistant reads as text, which is how it learns what to do next.</summary>
    private static string ToolResult(JsonNode? id, string text, bool isError) =>
        Result(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError,
        });

    /// <summary>A message as one line, whatever whitespace the server put in it.</summary>
    private static string OneLine(string json)
    {
        try
        {
            return JsonNode.Parse(json)?.ToJsonString() ?? json;
        }
        catch (JsonException)
        {
            return json.ReplaceLineEndings(" ");
        }
    }

    private async Task WriteAsync(StreamWriter writer, string line, CancellationToken cancellationToken)
    {
        await _writing.WaitAsync(cancellationToken);

        try
        {
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _writing.Release();
        }
    }
}

/// <summary>
/// What the bridge did, for a person finding out why an assistant cannot reach DevBuddy: never a
/// token, a message body or a result, only methods, statuses and reasons. Off unless a file is
/// named, because standard error is not shown by every assistant.
/// </summary>
internal sealed class BridgeLog(string? path)
{
    private readonly Lock _gate = new();

    public void Write(string line)
    {
        if (path is null)
        {
            return;
        }

        lock (_gate)
        {
            File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
    }
}
