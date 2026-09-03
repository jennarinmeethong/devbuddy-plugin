namespace DevBuddy.Infrastructure.Administration;

/// <summary>The shipped retention defaults from the Phase 11 schedule (SB-27), configurable per deployment.</summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>How long an audit event is kept before it is deleted outright.</summary>
    public TimeSpan AuditEventRetention { get; set; } = TimeSpan.FromDays(730);

    /// <summary>
    /// How long an evidence object may sit unreferenced by any revision before it is treated as
    /// orphaned. Newly captured evidence is not yet linked to the revision being drafted, so a
    /// grace period is what keeps that ordinary sequencing from looking like abandonment.
    /// </summary>
    public TimeSpan EvidenceOrphanGracePeriod { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// How long an archived record stays archived before it becomes eligible for deletion. Not
    /// auto-deleted: the schedule says "on owner request", so this only ever reports a count.
    /// </summary>
    public TimeSpan ArchivedRecordEligibility { get; set; } = TimeSpan.FromDays(730);
}
