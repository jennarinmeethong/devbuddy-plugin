namespace DevBuddy.Application.Pipeline;

/// <summary>
/// The outcome of one pipeline run. A result rather than an exception, because a denial is an
/// ordinary answer a host has to render, not an error condition.
/// </summary>
public sealed record UseCaseResult<TResponse>
{
    internal UseCaseResult(
        ExecutionOutcome outcome,
        TResponse? value,
        string reason,
        IReadOnlyList<string> validationErrors)
    {
        Outcome = outcome;
        Value = value;
        Reason = reason;
        ValidationErrors = validationErrors;
    }

    public ExecutionOutcome Outcome { get; }

    /// <summary>Present only when the outcome is Succeeded.</summary>
    public TResponse? Value { get; }

    public string Reason { get; }

    public IReadOnlyList<string> ValidationErrors { get; }

    public bool IsSuccess => Outcome == ExecutionOutcome.Succeeded;
}

/// <summary>
/// Factories for <see cref="UseCaseResult{TResponse}"/>. They live on a non-generic companion so
/// that <c>Success</c> can infer its type argument, and because static members on a generic type
/// have to be named through the full closed type at every call site.
/// </summary>
public static class UseCaseResult
{
    public static UseCaseResult<TResponse> Success<TResponse>(TResponse value) =>
        new(ExecutionOutcome.Succeeded, value, "Completed.", []);

    public static UseCaseResult<TResponse> Invalid<TResponse>(IReadOnlyList<string> errors) =>
        new(ExecutionOutcome.Invalid, default, "The request was not valid.", errors);

    public static UseCaseResult<TResponse> Denied<TResponse>(string reason) =>
        new(ExecutionOutcome.Denied, default, reason, []);

    public static UseCaseResult<TResponse> NotFound<TResponse>(string reason) =>
        new(ExecutionOutcome.NotFound, default, reason, []);

    public static UseCaseResult<TResponse> Rejected<TResponse>(string reason) =>
        new(ExecutionOutcome.Rejected, default, reason, []);

    public static UseCaseResult<TResponse> Blocked<TResponse>(string reason, IReadOnlyList<string> findings) =>
        new(ExecutionOutcome.Blocked, default, reason, findings);
}
