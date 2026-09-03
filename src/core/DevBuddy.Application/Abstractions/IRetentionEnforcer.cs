namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Applies the retention schedule to every durable copy this installation makes, not only the
/// primary database (SB-27).
/// <para>
/// Deliberately outside <see cref="IAdministrativeOperations"/> and the use-case pipeline. A
/// retention pass runs at installation scope — every workspace, every project — and there is no
/// caller to authorise it against, which is the same reason <c>restore</c> sits beside
/// <c>migrate</c> rather than behind a membership check. It is invoked from the console on an
/// operator's own schedule, not automatically: this system adds no in-process scheduler.
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
    int ArchivedRecordsEligibleForDeletion);
