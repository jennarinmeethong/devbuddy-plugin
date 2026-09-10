namespace DevBuddy.Application.Workers;

/// <summary>
/// How many paid calls one worker run may make, and what happens when they are gone.
/// <para>
/// Part of the design rather than an operational afterthought, per ADR-0013 and the AI Usage
/// section of `info.md`: a worker means per-call spend on a loop nobody is watching. A run that
/// throttled or retried when it ran out would keep spending; this refuses.
/// </para>
/// <para>
/// Deliberately a count of calls and not a sum of money. This code cannot know what a provider
/// charges, and a budget denominated in currency would be a number that drifts out of date
/// silently. An operator who knows the per-call price converts once.
/// </para>
/// </summary>
public sealed class WorkerBudget
{
    private int _spent;

    public WorkerBudget(int maximumCalls)
    {
        // Zero is a legitimate configuration and means "make no paid calls" — the way an operator
        // switches a worker off without removing it. Negative is not a smaller budget, it is a
        // mistake, and a mistake that silently became zero would look like a working switch.
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCalls);
        MaximumCalls = maximumCalls;
    }

    public int MaximumCalls { get; }

    public int Spent => _spent;

    public int Remaining => MaximumCalls - _spent;

    /// <summary>True once the budget is gone. A run that sees this stops rather than waits.</summary>
    public bool IsExhausted => Remaining <= 0;

    /// <summary>
    /// Takes <paramref name="calls"/> from the budget, or refuses and takes nothing.
    /// <para>
    /// All or nothing on purpose: a partial spend would leave the caller believing it had paid for
    /// work it cannot do, and the interesting case — the last call of a batch — is exactly where a
    /// partial answer would be wrong.
    /// </para>
    /// </summary>
    public bool TrySpend(int calls = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(calls);
        int requested = calls;

        if (requested > Remaining)
        {
            return false;
        }

        _spent += requested;
        return true;
    }
}
