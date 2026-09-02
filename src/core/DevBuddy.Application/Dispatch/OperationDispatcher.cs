using System.Text.Json;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Dispatch;

/// <summary>
/// Turns an operation name and a blob of JSON into a pipeline call.
/// <para>
/// One dispatcher, three hosts. The API, the MCP server, and the console all translate their
/// transport into a name plus arguments and hand it here, which is what keeps them thin and what
/// stops each of them growing its own idea of what an operation is. A host that wanted to bypass
/// the pipeline would have to bypass this too, and it cannot: <c>HandleAsync</c> is unreachable
/// from a host assembly.
/// </para>
/// <para>
/// Built per unit of work from resolved use cases, so it holds no service locator and the
/// Application layer keeps its independence from any container.
/// </para>
/// </summary>
public sealed class OperationDispatcher
{
    private readonly Dictionary<string, OperationBinding> _bindings;

    public OperationDispatcher(IEnumerable<OperationBinding> bindings)
    {
        Guard.NotNull(bindings, nameof(bindings));

        var map = new Dictionary<string, OperationBinding>(StringComparer.Ordinal);

        foreach (OperationBinding binding in bindings)
        {
            if (!map.TryAdd(binding.Descriptor.Name, binding))
            {
                throw new InvalidOperationException(
                    $"Two operations are registered under the name {binding.Descriptor.Name}.");
            }
        }

        _bindings = map;
    }

    /// <summary>Every operation this dispatcher can run, in catalogue order.</summary>
    public IReadOnlyList<UseCaseDescriptor> Operations =>
        [.. UseCaseCatalog.All.Where(descriptor => _bindings.ContainsKey(descriptor.Name))];

    /// <summary>
    /// The operations an AI caller may reach. The MCP server builds its tool list from this and
    /// nothing else, which is control SB-07.
    /// </summary>
    public IReadOnlyList<UseCaseDescriptor> AiOperations =>
        [.. Operations.Where(descriptor => descriptor.AiExposure == AiExposure.Allowed)];

    public bool Knows(string operation) => _bindings.ContainsKey(operation);

    /// <summary>The binding for one operation, for a host that needs its argument schema.</summary>
    public OperationBinding? Find(string operation) =>
        _bindings.TryGetValue(operation ?? string.Empty, out OperationBinding? binding) ? binding : null;

    /// <summary>
    /// Runs one operation. An unknown name is refused here rather than reaching the pipeline: the
    /// pipeline authorises operations, and an operation nobody defined has no permission to check.
    /// </summary>
    public async Task<DispatchResult> InvokeAsync(
        string operation,
        JsonElement arguments,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(caller, nameof(caller));

        if (!_bindings.TryGetValue(operation ?? string.Empty, out OperationBinding? binding))
        {
            return DispatchResult.Unknown(operation ?? string.Empty);
        }

        // The AI channel is checked here as well as in the pipeline. Two independent refusals for
        // the same rule is not redundancy: the MCP server should never have offered the tool, and
        // if it somehow did, this stops the request before a use case is even constructed.
        if (caller.Channel == AccessChannel.Ai && binding.Descriptor.AiExposure != AiExposure.Allowed)
        {
            return DispatchResult.Unknown(binding.Descriptor.Name);
        }

        return await binding.Invoke(arguments, caller, cancellationToken);
    }
}

/// <summary>
/// One operation, bound to the use case that performs it.
/// <para>
/// <paramref name="RequestType"/> is carried so a host can publish a schema for the arguments
/// without knowing anything about the use case. An MCP tool with no schema is a tool a model has
/// to guess at, and guessing produces malformed calls that look like refusals.
/// </para>
/// </summary>
public sealed record OperationBinding(
    UseCaseDescriptor Descriptor,
    Type RequestType,
    Type ResponseType,
    Func<JsonElement, CallerContext, CancellationToken, Task<DispatchResult>> Invoke)
{
    /// <summary>
    /// Wires a use case into the dispatcher: deserialise, run through the pipeline, serialise.
    /// <para>
    /// A malformed payload comes back as Invalid with the reason, not as an exception. What a
    /// caller sent is untrusted input like any other, and a stack trace is both useless to them
    /// and more than they should see.
    /// </para>
    /// </summary>
    public static OperationBinding For<TRequest, TResponse>(
        UseCase<TRequest, TResponse> useCase, UseCaseExecutor executor)
        where TRequest : IUseCaseRequest
    {
        Guard.NotNull(useCase, nameof(useCase));
        Guard.NotNull(executor, nameof(executor));

        return new OperationBinding(
            useCase.Descriptor,
            typeof(TRequest),
            typeof(TResponse),
            async (arguments, caller, cancellationToken) =>
            {
                TRequest request;

                try
                {
                    request = arguments.Deserialize<TRequest>(JsonConventions.Options)
                        ?? throw new JsonException("The arguments were empty.");
                }
                catch (JsonException failure)
                {
                    return DispatchResult.Invalid(useCase.Descriptor.Name, failure.Message);
                }
                catch (DomainValidationException failure)
                {
                    // A value object refused what arrived, such as a scope missing half of itself.
                    return DispatchResult.Invalid(useCase.Descriptor.Name, failure.Message);
                }

                UseCaseResult<TResponse> result =
                    await executor.ExecuteAsync(useCase, request, caller, cancellationToken);

                return DispatchResult.From(useCase.Descriptor.Name, result);
            });
    }
}

/// <summary>
/// What an operation produced, ready for any transport to render.
/// <para>
/// The payload is already JSON. A host decides how to frame it; none of them decides what it says.
/// </para>
/// </summary>
public sealed record DispatchResult(
    string Operation,
    ExecutionOutcome Outcome,
    JsonElement? Payload,
    string Reason,
    IReadOnlyList<string> Details)
{
    public bool IsSuccess => Outcome == ExecutionOutcome.Succeeded;

    public static DispatchResult From<TResponse>(string operation, UseCaseResult<TResponse> result)
    {
        Guard.NotNull(result, nameof(result));

        JsonElement? payload = result.IsSuccess
            ? JsonSerializer.SerializeToElement(result.Value, JsonConventions.Options)
            : null;

        return new DispatchResult(
            operation, result.Outcome, payload, result.Reason, result.ValidationErrors);
    }

    public static DispatchResult Invalid(string operation, string reason) =>
        new(operation, ExecutionOutcome.Invalid, null, reason, []);

    /// <summary>
    /// An operation that does not exist, or that this caller may not know exists.
    /// <para>
    /// Reported the same way in both cases on purpose. Telling an AI caller that
    /// <c>publish_record</c> is real but off limits is a small disclosure that costs nothing to
    /// avoid.
    /// </para>
    /// </summary>
    public static DispatchResult Unknown(string operation) =>
        new(operation, ExecutionOutcome.NotFound, null, $"No operation named {operation}.", []);
}
