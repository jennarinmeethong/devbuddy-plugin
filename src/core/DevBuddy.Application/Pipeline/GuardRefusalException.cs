namespace DevBuddy.Application.Pipeline;

/// <summary>
/// Thrown by an adapter when a boundary guard refused what a request pointed at: a path that
/// resolves outside the authorised root, or a destination the outbound allow-list does not carry.
/// The pipeline turns it into a Denied result and audits it as a denied access.
/// <para>
/// An adapter throws this, rather than letting the guard's own exception escape, because only the
/// adapter knows that the guard is what spoke. An <see cref="UnauthorizedAccessException"/> from
/// the operating system refusing a directory looks identical, and reporting that as a caller
/// reaching too far would send an investigation after the wrong person.
/// </para>
/// <para>
/// The message is written for the caller and must not repeat the path or address that was
/// refused. <see cref="RefusedBy"/> is a fixed vocabulary — <c>path-guard</c>, <c>url-guard</c> —
/// and is the only part that reaches the audit details.
/// </para>
/// </summary>
public sealed class GuardRefusalException : Exception
{
    public GuardRefusalException()
    {
    }

    public GuardRefusalException(string message)
        : base(message)
    {
    }

    public GuardRefusalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public GuardRefusalException(string refusedBy, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        RefusedBy = refusedBy;
    }

    /// <summary>Which guard refused. A fixed name, never anything the caller supplied.</summary>
    public string RefusedBy { get; } = "guard";
}
