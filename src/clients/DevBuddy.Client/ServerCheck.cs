using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;

namespace DevBuddy.Client;

internal enum CheckOutcome
{
    Accepted,
    TokenRefused,
    WrongWorkspace,
    NoMcpEndpoint,
    CertificateNotTrusted,
    Unreachable,
    Unexpected,
}

internal sealed record CheckResult(CheckOutcome Outcome, string Detail)
{
    public bool Succeeded => Outcome == CheckOutcome.Accepted;

    /// <summary>What to tell the person, including what to do about it.</summary>
    public string Explain(string origin) => Outcome switch
    {
        CheckOutcome.Accepted => $"{origin} accepts the token in this workspace.",
        CheckOutcome.TokenRefused =>
            $"{origin} refused the token: it is mistyped, expired, revoked, or from before tokens had a workspace. "
            + "Mint a new one under Plugin access and run `devbuddy token set`.",
        CheckOutcome.WrongWorkspace =>
            $"{origin} accepts the token, but not in this workspace. Mint one in this workspace under Plugin access.",
        CheckOutcome.NoMcpEndpoint =>
            $"{origin} has no MCP endpoint at /mcp. The server is from before MCP moved there, or the gateway does not pass /mcp to it.",
        CheckOutcome.CertificateNotTrusted =>
            $"This machine does not trust {origin}'s certificate. Install the gateway's root CA, then try again. {Detail}",
        CheckOutcome.Unreachable => $"{origin} could not be reached: {Detail}",
        _ => $"{origin} answered something unexpected: {Detail}",
    };
}

/// <summary>
/// Asks a server whether a token works in a workspace, the way an assistant would: over MCP, at
/// <c>/mcp</c>, with the token as the bearer.
/// </summary>
internal sealed class ServerCheck(HttpMessageHandler handler)
{
    public async Task<CheckResult> VerifyAsync(
        string origin, string token, Guid workspace, CancellationToken cancellationToken)
    {
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(20) };
        Uri endpoint = ServerOrigin.McpEndpoint(origin);

        try
        {
            (HttpStatusCode initialize, _) = await PostAsync(http, endpoint, token, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { },
                    clientInfo = new { name = "devbuddy-client", version = "1" },
                },
            }, cancellationToken);

            if (initialize == HttpStatusCode.Unauthorized)
            {
                return new CheckResult(CheckOutcome.TokenRefused, string.Empty);
            }

            if (initialize is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                return new CheckResult(CheckOutcome.NoMcpEndpoint, string.Empty);
            }

            if (initialize != HttpStatusCode.OK)
            {
                return new CheckResult(CheckOutcome.Unexpected, $"HTTP {(int)initialize} to initialize.");
            }

            (HttpStatusCode call, string body) = await PostAsync(http, endpoint, token, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new { name = "list_projects", arguments = new { workspaceId = workspace } },
            }, cancellationToken);

            if (call != HttpStatusCode.OK)
            {
                return new CheckResult(CheckOutcome.Unexpected, $"HTTP {(int)call} to list_projects.");
            }

            return ReadToolResult(body);
        }
        catch (HttpRequestException exception) when (exception.InnerException is AuthenticationException)
        {
            return new CheckResult(CheckOutcome.CertificateNotTrusted, exception.InnerException.Message);
        }
        catch (HttpRequestException exception)
        {
            return new CheckResult(CheckOutcome.Unreachable, exception.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CheckResult(CheckOutcome.Unreachable, "it did not answer within 20 seconds.");
        }
    }

    private static async Task<(HttpStatusCode Status, string Body)> PostAsync(
        HttpClient http, Uri endpoint, string token, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(body) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// A tool call answers 200 whether or not the operation was allowed; a refusal is a result
    /// marked as an error, and a token from another workspace is refused naming the credential.
    /// </summary>
    private static CheckResult ReadToolResult(string body)
    {
        string json = body.TrimStart().StartsWith('{') ? body : LastEvent(body);

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement result = document.RootElement.GetProperty("result");
            bool refused = result.TryGetProperty("isError", out JsonElement isError) && isError.GetBoolean();

            if (!refused)
            {
                return new CheckResult(CheckOutcome.Accepted, string.Empty);
            }

            string text = result.GetProperty("content").EnumerateArray()
                .Select(part => part.TryGetProperty("text", out JsonElement value) ? value.GetString() : null)
                .FirstOrDefault(value => value is not null) ?? string.Empty;

            return text.Contains("credential", StringComparison.OrdinalIgnoreCase)
                ? new CheckResult(CheckOutcome.WrongWorkspace, text)
                : new CheckResult(CheckOutcome.Unexpected, text);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new CheckResult(CheckOutcome.Unexpected, "the answer to list_projects was not a tool result.");
        }
    }

    private static string LastEvent(string stream) =>
        stream.Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim())
            .LastOrDefault() ?? string.Empty;
}
