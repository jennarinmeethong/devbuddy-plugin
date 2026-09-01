namespace DevBuddy.Domain.Common;

/// <summary>
/// Base type for every rule the domain enforces itself. Catching this type catches a
/// broken invariant, never an infrastructure failure.
/// </summary>
public class DomainException : Exception
{
    public DomainException()
    {
    }

    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A value handed to the domain is missing or malformed.
/// </summary>
public sealed class DomainValidationException : DomainException
{
    public DomainValidationException()
    {
    }

    public DomainValidationException(string message)
        : base(message)
    {
    }

    public DomainValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// An operation was attempted from a state that does not allow it.
/// </summary>
public sealed class InvalidTransitionException : DomainException
{
    public InvalidTransitionException()
    {
    }

    public InvalidTransitionException(string message)
        : base(message)
    {
    }

    public InvalidTransitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Publication or approval was attempted against a revision other than the one reviewed.
/// This is control SB-23: approval binds to the exact content revision a human read.
/// </summary>
public sealed class ApprovalRevisionMismatchException : DomainException
{
    public ApprovalRevisionMismatchException()
    {
    }

    public ApprovalRevisionMismatchException(string message)
        : base(message)
    {
    }

    public ApprovalRevisionMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
