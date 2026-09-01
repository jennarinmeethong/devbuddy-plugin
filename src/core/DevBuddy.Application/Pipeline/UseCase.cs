using DevBuddy.Application.Security;

namespace DevBuddy.Application.Pipeline;

/// <summary>
/// Base class for every operation in the system.
/// <para>
/// HandleAsync is protected internal on purpose. That makes it reachable from
/// <see cref="UseCaseExecutor"/>, which lives in this assembly, and unreachable from the API,
/// MCP, and console hosts, which do not. A host physically cannot call a use case without going
/// through the pipeline, so the authorization, redaction, and audit stages are not something a
/// future contributor can forget to add: the compiler stops them.
/// </para>
/// </summary>
public abstract class UseCase<TRequest, TResponse> : IUseCase
    where TRequest : IUseCaseRequest
{
    public abstract UseCaseDescriptor Descriptor { get; }

    /// <summary>
    /// The work itself. By the time this runs, the request has been validated, the caller has an
    /// identity, and an explicit allow decision has been made for this permission and scope.
    /// </summary>
    protected internal abstract Task<TResponse> HandleAsync(
        TRequest request, CallerContext caller, CancellationToken cancellationToken);
}
