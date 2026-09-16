namespace DevBuddy.Domain.Auditing;

/// <summary>
/// The channel a request arrived on, as the audit trail records it.
/// <para>
/// A copy of the application's access channel rather than a reference to it, because the domain
/// references nothing. The numbers match on purpose, so a stored value means the same thing
/// whichever of the two a reader has in mind.
/// </para>
/// <para>
/// Recorded because the actor alone cannot answer the question. A machine token's owner is an
/// ordinary user, so a draft an assistant created through that token and one the same person
/// typed into the web interface left identical audit rows until this existed.
/// </para>
/// </summary>
public enum AuditChannel
{
    /// <summary>A signed-in person, through the API or the web interface.</summary>
    Human = 1,

    /// <summary>Claude or Codex through the MCP server, or a worker job that sends content to a model.</summary>
    Ai = 2,

    /// <summary>A scheduled or administrative process with no interactive caller.</summary>
    InternalSystem = 3,
}
