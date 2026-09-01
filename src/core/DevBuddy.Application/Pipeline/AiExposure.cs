namespace DevBuddy.Application.Pipeline;

/// <summary>
/// Whether a use case may ever be reached through the AI channel.
/// <para>
/// info.md permits only search, get, analyse, create a draft, and generate a handover. Everything
/// else stays under human or internal-system control. This is declared once, in the descriptor,
/// and checked twice: structurally here, and again by the MCP server, which never lists a denied
/// use case as a tool at all (control SB-07).
/// </para>
/// </summary>
public enum AiExposure
{
    /// <summary>Never reachable from the AI channel, whatever a tool definition might say.</summary>
    Denied = 1,

    /// <summary>May be exposed as an MCP tool, subject to per-project AI access.</summary>
    Allowed = 2,
}
