namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Applies the retention schedule to every durable copy this installation makes, not only the
/// primary database (SB-27).
/// <para>
/// Deliberately outside <see cref="IAdministrativeOperations"/> and the use-case pipeline. A
/// retention pass runs at installation scope — every workspace, every project — and there is no
/// caller to authorise it against, which is the same reason <c>restore</c> sits beside
/// <c>migrate</c> rather than behind a membership check.
/// </para>
/// <para>
/// Invoked from the console: once as <c>retention</c>, or repeatedly as <c>retention --every</c>,
/// which is how the shipped stack schedules its own sweep now that Phase 12B stopped leaving that
/// to whatever the operator built. The loop is a loop and nothing more — it acquires no caller, no
/// membership and no tenant context, because a scheduler that acquired one would be the
/// installation-wide superuser this system does not have.
/// </para>
/// </summary>
public interface IRetentionEnforcer
{
    Task<RetentionReport> ApplyAsync(CancellationToken cancellationToken);
}

/// <summary>
/// What one retention pass actually removed, per copy. A count of zero means the rule ran and
/// found nothing due, not that the rule was skipped.
/// </summary>
public sealed record RetentionReport(
    int AuditEventsDeleted,
    int OrphanedEvidenceDeleted,
    int BackupsDeleted,
    int ExportsDeleted,
    int LogFilesDeleted,
    int ArchivedRecordsEligibleForDeletion);
