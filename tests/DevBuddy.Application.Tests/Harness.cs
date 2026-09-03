using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;

namespace DevBuddy.Application.Tests;

/// <summary>
/// A pipeline wired to fakes, permissive by default, so a behaviour test says what it is about
/// rather than repeating four lines of construction.
/// </summary>
internal sealed class Harness
{
    public Harness()
    {
        Executor = new UseCaseExecutor(Authorization, Audit, Ports, Ports, Ports, Ports, Ports);
    }

    public FakePorts Ports { get; } = new();

    public FakeAuthorizationService Authorization { get; } = new();

    public FakeAuditSink Audit { get; } = new();

    public UseCaseExecutor Executor { get; }

    public Task<UseCaseResult<TResponse>> RunAsync<TRequest, TResponse>(
        UseCase<TRequest, TResponse> useCase, TRequest request, CallerContext? caller = null)
        where TRequest : IUseCaseRequest =>
        Executor.ExecuteAsync(useCase, request, caller ?? TestData.Human, CancellationToken.None);

    /// <summary>Runs and unwraps, failing the test if the pipeline did not succeed.</summary>
    public async Task<TResponse> SucceedAsync<TRequest, TResponse>(
        UseCase<TRequest, TResponse> useCase, TRequest request, CallerContext? caller = null)
        where TRequest : IUseCaseRequest
    {
        UseCaseResult<TResponse> result = await RunAsync(useCase, request, caller);

        Assert.True(
            result.IsSuccess,
            $"Expected success but got {result.Outcome}: {result.Reason} "
            + string.Join("; ", result.ValidationErrors));

        return result.Value!;
    }
}
