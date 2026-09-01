namespace DevBuddy.Application.Security;

/// <summary>
/// How a request arrived. This is not decoration: the pipeline refuses AI-channel calls to any
/// use case not marked as safe for AI, and the authorization service applies the per-project AI
/// access policy on top. A host cannot widen its own channel, because the channel is decided by
/// which host is running, not by anything in the request.
/// </summary>
public enum AccessChannel
{
    /// <summary>A signed-in person, through the API or the web UI.</summary>
    Human = 1,

    /// <summary>Claude or Codex, through the MCP server.</summary>
    Ai = 2,

    /// <summary>An internal scheduled or administrative process with no interactive caller.</summary>
    InternalSystem = 3,
}
