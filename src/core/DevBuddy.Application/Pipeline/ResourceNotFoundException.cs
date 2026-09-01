namespace DevBuddy.Application.Pipeline;

/// <summary>
/// Thrown by a use case when an authorised lookup finds nothing. The pipeline turns it into a
/// NotFound result, so use cases can read linearly instead of threading a null through every
/// step.
/// </summary>
public sealed class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException()
    {
    }

    public ResourceNotFoundException(string message)
        : base(message)
    {
    }

    public ResourceNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
