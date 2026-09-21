using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Reading the audit history. Separate from <see cref="IAuditSink"/> so that the vast majority
/// of the system, which only writes audit entries, cannot read them.
/// </summary>
public interface IAuditReader
{
    /// <summary>
    /// Entries in one project and window, newest first. A channel narrows the answer to entries
    /// recorded on that channel; an entry written before channels were recorded matches no channel
    /// filter, because it cannot say which one it was.
    /// <para>
    /// <paramref name="channelNotRecorded"/> asks for exactly those entries instead: the ones with
    /// no channel, written before the column existed (Phase 13, D4). It is its own question, never a
    /// channel value, because treating "not recorded" as one of the three would be the guess the
    /// column was added to avoid.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<AuditEvent>> QueryAsync(
        ProjectScope scope,
        DateTimeOffset occurredFrom,
        DateTimeOffset occurredUntil,
        UserId? actorId,
        AuditChannel? channel,
        bool channelNotRecorded,
        CancellationToken cancellationToken);
}
