using DevBuddy.Domain.Auditing;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Append-only audit writing. Implementations must not copy the sensitive payload an action
/// touched into the audit store: an AuditEvent references a resource, it does not contain it
/// (control SB-19).
/// </summary>
public interface IAuditSink
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}
