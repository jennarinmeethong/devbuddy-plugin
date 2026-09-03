namespace DevBuddy.Infrastructure.Administration;

/// <summary>
/// Where backups are written, and how many are kept.
/// <para>
/// A directory rather than an object store, because a backup that lives inside the system it backs
/// up is not a backup. The operator mounts something outside the containers and points this at it.
/// </para>
/// </summary>
public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public string RootPath { get; set; } = "./.data/backups";

    /// <summary>
    /// How long a backup is kept before it is a candidate for deletion. A backup is a full copy of
    /// everything, so the retention schedule applies to it as much as to the primary store
    /// (ADR-0009, SB-27).
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(90);
}
