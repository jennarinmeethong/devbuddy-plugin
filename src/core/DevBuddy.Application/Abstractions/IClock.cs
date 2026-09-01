namespace DevBuddy.Application.Abstractions;

/// <summary>
/// The only source of time in the application layer. Injected rather than read from
/// DateTimeOffset.UtcNow so that approval timestamps and retention sweeps are testable.
/// </summary>
public interface IClock
{
    /// <summary>Always UTC. The domain rejects any other offset.</summary>
    DateTimeOffset UtcNow { get; }
}
