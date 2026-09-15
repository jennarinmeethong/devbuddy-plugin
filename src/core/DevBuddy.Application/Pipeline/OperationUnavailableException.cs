namespace DevBuddy.Application.Pipeline;

/// <summary>
/// Thrown by an adapter when this installation, as configured, cannot answer the request: no
/// analysis root is set, an object is stored in a form the reader does not implement, a repository
/// has no provider address. The pipeline turns it into a Rejected result carrying the message, and
/// audits the attempt as failed.
/// <para>
/// Distinct from <see cref="ResourceNotFoundException"/> on purpose. "Nothing is mounted for this
/// repository" is an answer about the resource; "no analysis root is configured" is an answer
/// about the installation, and telling the two apart is what lets a caller know whether to ask an
/// operator or to check what they asked for.
/// </para>
/// <para>
/// The message reaches the caller and, when it is short enough, the audit details. It must name
/// the setting or limitation and never carry content read from a repository.
/// </para>
/// </summary>
public sealed class OperationUnavailableException : Exception
{
    public OperationUnavailableException()
    {
    }

    public OperationUnavailableException(string message)
        : base(message)
    {
    }

    public OperationUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
