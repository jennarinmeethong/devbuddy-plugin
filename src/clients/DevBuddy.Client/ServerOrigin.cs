namespace DevBuddy.Client;

/// <summary>
/// A DevBuddy server as the registry records it: scheme, host and port, and nothing else.
/// </summary>
internal static class ServerOrigin
{
    /// <summary>
    /// HTTPS, or plain HTTP to this machine only. A token sent over plain HTTP across a network
    /// can be read by anybody on the path, and a machine token is good for a year.
    /// </summary>
    public static bool TryParse(string? text, out string origin, out string problem)
    {
        origin = string.Empty;

        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            problem = $"'{text}' is not an http or https address.";
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            problem = $"'{text}' is plain HTTP to another machine. Use https://, so the token is not sent in clear.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            problem = "The server address must not carry a user name or password.";
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        problem = string.Empty;
        return true;
    }

    /// <summary>The MCP endpoint of a server, which the gateway passes to the MCP service.</summary>
    public static Uri McpEndpoint(string origin) => new($"{origin}/mcp");

    /// <summary>Whether an MCP URL an assistant is about to use is on this server.</summary>
    public static bool IsOn(string? url, string origin) =>
        TryParse(url, out string candidate, out _)
        && string.Equals(candidate, origin, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a machine token looks like, so a paste with anything around it is caught.</summary>
internal static class TokenFormat
{
    /// <summary>32 random bytes, base64url without padding: what the server mints.</summary>
    public const int Length = 43;

    public static bool IsValid(string? token) =>
        token is { Length: Length } && token.All(character =>
            character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');
}
