using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Analysis;

/// <summary>
/// A ceiling on how many analyses run at once, across the whole process, and the slots that
/// enforce it.
/// <para>
/// The per-analysis limits already bound one run: a file ceiling, a size ceiling, and a timeout
/// (SB-22). None of them bounds twenty runs starting together, and analysis is the most expensive
/// thing this system does — it walks a working copy and reads files. Twenty at once is a slow
/// system for everybody, and it is trivially reachable by an assistant looping over projects.
/// </para>
/// </summary>
/// <remarks>
/// A singleton, and that is the point. The decorator around the analyser is created per request,
/// and a semaphore created with it would permit one analysis per request, which is no limit at all.
/// Separating the state from the decorator is what makes the ceiling process-wide.
/// </remarks>
internal sealed class AnalysisSlots : IDisposable
{
    private readonly SemaphoreSlim _slots;

    public AnalysisSlots(IOptions<AnalysisOptions> options)
    {
        AnalysisOptions settings = Guard.NotNull(options, nameof(options)).Value;

        _slots = new SemaphoreSlim(Math.Max(1, settings.MaxConcurrentAnalyses));
        QueueTimeout = settings.QueueTimeout;
    }

    public TimeSpan QueueTimeout { get; }

    public Task<bool> WaitAsync(CancellationToken cancellationToken) =>
        _slots.WaitAsync(QueueTimeout, cancellationToken);

    public void Release() => _slots.Release();

    public void Dispose() => _slots.Dispose();
}

/// <summary>
/// Takes a slot, runs, releases.
/// <para>
/// A decorator rather than a check inside the analyser, so the limit cannot be forgotten by a
/// second implementation of the port and does not have to be repeated in each of the eight
/// analysis kinds.
/// </para>
/// </summary>
internal sealed class ConcurrentAnalysisLimit : ICodeAnalyzer
{
    private readonly ICodeAnalyzer _inner;
    private readonly AnalysisSlots _slots;

    public ConcurrentAnalysisLimit(ICodeAnalyzer inner, AnalysisSlots slots)
    {
        _inner = Guard.NotNull(inner, nameof(inner));
        _slots = Guard.NotNull(slots, nameof(slots));
    }

    public async Task<AnalysisReport> AnalyzeAsync(
        AnalysisKind kind,
        ProjectScope scope,
        SourceRepositoryId? repositoryId,
        string? target,
        CancellationToken cancellationToken)
    {
        if (!await _slots.WaitAsync(cancellationToken))
        {
            // Refused rather than queued indefinitely. A caller waiting forever behind a queue is
            // a caller who cannot tell a busy system from a broken one, and an assistant that got
            // no answer will simply ask again.
            throw new AnalysisBusyException(
                "Too many analyses are already running. Try again shortly.");
        }

        try
        {
            return await _inner.AnalyzeAsync(kind, scope, repositoryId, target, cancellationToken);
        }
        finally
        {
            _slots.Release();
        }
    }
}

/// <summary>
/// Raised when the concurrency ceiling is reached. A domain exception, so the pipeline turns it
/// into a refusal with its reason rather than a stack trace.
/// </summary>
public sealed class AnalysisBusyException : DomainException
{
    public AnalysisBusyException()
    {
    }

    public AnalysisBusyException(string message)
        : base(message)
    {
    }

    public AnalysisBusyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
