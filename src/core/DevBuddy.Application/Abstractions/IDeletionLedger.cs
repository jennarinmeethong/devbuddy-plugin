using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Remembers, beside the backups, which projects were deleted and when (Phase 13, D7).
/// <para>
/// A backup is a copy of what existed when it was taken. Restoring one taken before a project was
/// deleted used to bring the project back, content, evidence and all, which is the residual
/// <c>info.md</c> named under Accepted Security Limitations. The ledger lives on the same volume
/// as the backups, so it survives the loss a backup exists for, and a restore re-applies every
/// deletion recorded after the backup was taken.
/// </para>
/// <para>
/// It holds identifiers and a time, never a name or any content: it would otherwise be a copy of
/// what was deleted, which is the thing it exists to prevent.
/// </para>
/// </summary>
public interface IDeletionLedger
{
    Task RecordProjectDeletedAsync(ProjectScope scope, CancellationToken cancellationToken);
}
