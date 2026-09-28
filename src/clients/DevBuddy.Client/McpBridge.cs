using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevBuddy.Client;

/// <summary>
/// MCP over stdio on one side and DevBuddy's HTTP transport on the other, for an assistant that
/// starts a local server but will not run a header helper: Cowork reads <c>url</c>, <c>headers</c>
/// and <c>oauth</c> from a plugin's <c>.mcp.json</c> and nothing else.
/// <para>
/// Each line on standard input is one JSON-RPC message, posted as it is to the checkout's own
/// server, never to an address it was given, with the checkout's token read from the store for
/// every request so a replaced token is picked up. Every message in the answer, JSON or an event
/// stream, is written back as one line. A request the server or the network refuses is answered
/// with a JSON-RPC error saying why, so the assistant can tell the person.
/// </para>
/// </summary>
internal sealed class McpBridge(ClientContext context, Checkout checkout, BridgeLog log) : IDisposable
{
    /// <summary>Longer than any operation should take, so a slow analysis is not cut off.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly Lock _tools = new();
    private readonly HashSet<string> _workspaceArgument = new(StringComparer.Ordinal);
    private readonly HashSet<string> _workspaceInScope = new(StringComparer.Ordinal);
    private string? _protocolVersion;

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

            Task forwarded = ForwardAsync(http, line, writer, cancellationToken);

            // initialize settles the protocol version every later request carries, and a client
            // sends nothing else before its answer. Everything after it is concurrent, as the
            // protocol allows: one slow analysis must not hold up a search.
            if (IsInitialize(line))
            {
                await forwarded;
            }
            else
            {
                inFlight.Add(forwarded);
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

    private async Task ForwardAsync(HttpClient http, string line, StreamWriter writer, CancellationToken cancellationToken)
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

        string body = method == "tools/call" && incoming is not null ? FillWorkspace(incoming) : line;
        string? token;

        try
        {
            token = context.Store.Read(CredentialStores.KeyFor(checkout.Server, checkout.Workspace));
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
                $"DevBuddy: no token is stored for {checkout.Server}, workspace {checkout.Workspace:D}. Run `devbuddy token set`.",
                cancellationToken);
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ServerOrigin.McpEndpoint(checkout.Server))
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
                await RefuseAsync(writer, id, method, Refusal(response.StatusCode), cancellationToken);
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
                new CheckResult(CheckOutcome.CertificateNotTrusted, exception.InnerException.Message).Explain(checkout.Server),
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            await RefuseAsync(
                writer, id, method,
                new CheckResult(CheckOutcome.Unreachable, exception.Message).Explain(checkout.Server),
                cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RefuseAsync(
                writer, id, method,
                $"DevBuddy: {checkout.Server} did not answer within {RequestTimeout.TotalMinutes:0} minutes.",
                cancellationToken);
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
    /// The answer to <c>initialize</c>, with the workspace and default project added to the
    /// server's instructions. An assistant inside a sandbox cannot run <c>devbuddy show</c> to
    /// find them, and every tool needs the workspace.
    /// </summary>
    private string Introduce(string answer)
    {
        if (JsonNode.Parse(answer) is not JsonObject message || message["result"] is not JsonObject result)
        {
            return answer;
        }

        _protocolVersion = result["protocolVersion"]?.GetValue<string>() ?? _protocolVersion;

        string project = checkout.Project is { } id
            ? $"The default project is {id:D}."
            : "No default project is registered; call list_projects to find one.";
        string introduction =
            $"This DevBuddy connection works in workspace {checkout.Workspace:D} and no other, and fills "
            + $"it in on every call, so no tool asks for it. {project} It is a registration, not DevBuddy content.";

        string? existing = result["instructions"]?.GetValue<string>();
        result["instructions"] = string.IsNullOrEmpty(existing) ? introduction : $"{existing}\n\n{introduction}";

        return message.ToJsonString();
    }

    /// <summary>
    /// The tool list with the workspace taken out of every schema. The token works in one
    /// workspace, so the bridge supplies it; an assistant asked for it would have to guess, and
    /// Cowork showed one does: it does not pass a server's instructions on.
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
                lock (_tools)
                {
                    _workspaceArgument.Add(name);
                }
            }

            if (schema["properties"]?["scope"] is JsonObject scope && Remove(scope, "workspaceId"))
            {
                lock (_tools)
                {
                    _workspaceInScope.Add(name);
                }
            }
        }

        return tools.Root.ToJsonString();
    }

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
    /// A tool call with the checkout's workspace put where the tool takes it, replacing whatever
    /// the assistant wrote there. That grants nothing: the token is refused in any other workspace.
    /// </summary>
    private string FillWorkspace(JsonObject call)
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

        lock (_tools)
        {
            inScope = _workspaceInScope.Contains(name);
            asArgument = _workspaceArgument.Contains(name);
        }

        string workspace = checkout.Workspace.ToString("D");

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

    private string Refusal(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => $"DevBuddy: {new CheckResult(CheckOutcome.TokenRefused, string.Empty).Explain(checkout.Server)}",
        HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed =>
            $"DevBuddy: {new CheckResult(CheckOutcome.NoMcpEndpoint, string.Empty).Explain(checkout.Server)}",
        HttpStatusCode.TooManyRequests => $"DevBuddy: {checkout.Server} is limiting requests. Wait a minute and try again.",
        _ => $"DevBuddy: {checkout.Server} answered HTTP {(int)status}.",
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
