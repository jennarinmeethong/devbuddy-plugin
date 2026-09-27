using System.Net;
using System.Text;

namespace DevBuddy.Client.Tests;

/// <summary>
/// A temporary home, a file credential store, a scripted server and a scripted token, so a command
/// runs exactly as it would for a person and touches nothing of theirs.
/// </summary>
internal sealed class ClientHarness : IDisposable
{
    public const string Server = "https://devbuddy.example.test:5010";

    public static readonly Guid Workspace = Guid.Parse("5cd6f516-5b45-45ed-a5c9-6cdbb60df2dc");

    /// <summary>A well-formed token: 43 base64url characters.</summary>
    public static readonly string GoodToken = new string('A', 42) + "Q";

    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    public ClientHarness()
    {
        Root = Directory.CreateTempSubdirectory("devbuddy-client-").FullName;
        Home = new ClientHome(Path.Combine(Root, "home"));
        Store = new FileCredentialStore(Home);
        WorkingDirectory = Root;
    }

    public string Root { get; }

    public ClientHome Home { get; }

    public FileCredentialStore Store { get; }

    public FakeServer Remote { get; } = new();

    public string WorkingDirectory { get; set; }

    public Queue<string?> Tokens { get; } = new();

    public int TokenPrompts { get; private set; }

    public StringWriter Out { get; private set; } = new();

    public StringWriter Error { get; private set; } = new();

    public void SetEnvironment(string name, string? value) => _environment[name] = value;

    /// <summary>A folder with a <c>.git</c> directory, as a clone has.</summary>
    public string Checkout(string name)
    {
        string path = Path.Combine(Root, name);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        return path;
    }

    public async Task<int> RunAsync(params string[] args)
    {
        Out = new StringWriter();
        Error = new StringWriter();

        var context = new ClientContext(
            Home,
            Store,
            Remote,
            Out,
            Error,
            name => _environment.GetValueOrDefault(name),
            WorkingDirectory,
            _ =>
            {
                TokenPrompts++;
                return Tokens.Count > 0 ? Tokens.Dequeue() : null;
            });

        return await Commands.Build(context).Parse(args, Commands.Parsing).InvokeAsync();
    }

    public void Dispose()
    {
        Remote.Dispose();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is not a failure of the test.
        }
    }
}

/// <summary>
/// Answers MCP the way the real transport does: 401 for a bearer it does not know, and a tool
/// result, possibly a refusal naming the credential, for one it does.
/// </summary>
internal sealed class FakeServer : HttpMessageHandler
{
    public HashSet<string> Accepted { get; } = new(StringComparer.Ordinal);

    /// <summary>Tokens the server accepts but refuses in this workspace.</summary>
    public HashSet<string> OtherWorkspace { get; } = new(StringComparer.Ordinal);

    public List<Uri> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);

        string? bearer = request.Headers.Authorization?.Parameter;
        bool known = bearer is not null && (Accepted.Contains(bearer) || OtherWorkspace.Contains(bearer));

        if (request.RequestUri!.AbsolutePath != "/mcp")
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (!known)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        string body = await request.Content!.ReadAsStringAsync(cancellationToken);

        string answer = body.Contains("\"initialize\"", StringComparison.Ordinal)
            ? """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18"}}"""
            : OtherWorkspace.Contains(bearer!)
                ? """{"jsonrpc":"2.0","id":2,"result":{"isError":true,"content":[{"type":"text","text":"Refused: the credential is not valid in this workspace."}]}}"""
                : """{"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"{\"projects\":[]}"}]}}""";

        // As an event stream, the way the SDK answers when the client accepts one.
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"event: message\ndata: {answer}\n\n", Encoding.UTF8, "text/event-stream"),
        };
    }
}
