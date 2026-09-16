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
    /// </summary>
    Task<IReadOnlyList<AuditEvent>> QueryAsync(
        ProjectScope scope,
        DateTimeOffset occurredFrom,
        DateTimeOffset occurredUntil,
        UserId? actorId,
        AuditChannel? channel,
        CancellationToken cancellationToken);
}
