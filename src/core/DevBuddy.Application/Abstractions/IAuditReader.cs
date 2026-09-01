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
    Task<IReadOnlyList<AuditEvent>> QueryAsync(
        ProjectScope scope,
        DateTimeOffset occurredFrom,
        DateTimeOffset occurredUntil,
        UserId? actorId,
        CancellationToken cancellationToken);
}
