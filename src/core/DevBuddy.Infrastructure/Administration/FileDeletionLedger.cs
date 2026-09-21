using System.Globalization;
using System.Text.Json;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Administration;

/// <summary>
/// The deletion ledger as a line-per-entry file, <c>deletions.jsonl</c>, in the backup root.
/// <para>
/// A file beside the backups rather than a table, because the database is exactly what a restore
/// replaces: a ledger kept there would be lost with it. Each line is one deletion, identifiers and
/// a time. A line that cannot be read is counted and skipped rather than stopping a restore.
/// </para>
/// <para>
/// Failing to write is logged, not thrown. A deletion must not fail because the backup volume is
/// missing, as it is under a plain <c>docker run</c>; the restore then reports that it found no
/// ledger, which is the honest answer.
/// </para>
/// </summary>
internal sealed class FileDeletionLedger(
    IOptions<BackupOptions> options, IClock clock, ILogger<FileDeletionLedger> logger) : IDeletionLedger
{
    public const string FileName = "deletions.jsonl";

    private readonly BackupOptions _options = Guard.NotNull(options, nameof(options)).Value;
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));
    private readonly ILogger<FileDeletionLedger> _logger = Guard.NotNull(logger, nameof(logger));

    private string PathToLedger => Path.Combine(Path.GetFullPath(_options.RootPath), FileName);

    public async Task RecordProjectDeletedAsync(ProjectScope scope, CancellationToken cancellationToken)
    {
        try
        {
            LedgerRead existing = await ReadAsync(cancellationToken);

            // Recorded once. A restore re-applying a deletion goes through the same delete and
            // would otherwise add the same line again on every restore.
            if (existing.Entries.Any(entry =>
                    entry.WorkspaceId == scope.WorkspaceId.Value && entry.ProjectId == scope.ProjectId.Value))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PathToLedger)!);

            string line = JsonSerializer.Serialize(
                new DeletionEntry(scope.WorkspaceId.Value, scope.ProjectId.Value, _clock.UtcNow));

            await File.AppendAllTextAsync(PathToLedger, line + "\n", cancellationToken);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                "A project deletion could not be written to the deletion ledger beside the backups ({Reason}). "
                + "Restoring a backup taken before it would bring the project back.",
                failure.GetType().Name);
        }
    }

    /// <summary>Every readable entry, how many lines could not be read, and whether there is a ledger at all.</summary>
    public async Task<LedgerRead> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(PathToLedger))
        {
            return new LedgerRead(false, [], 0);
        }

        List<DeletionEntry> entries = [];
        int unreadable = 0;

        foreach (string line in await File.ReadAllLinesAsync(PathToLedger, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<DeletionEntry>(line) is { } entry)
                {
                    entries.Add(entry);
                    continue;
                }
            }
            catch (JsonException)
            {
                // Counted as unreadable below, and the restore reports how many.
            }

            unreadable++;
        }

        return new LedgerRead(true, entries, unreadable);
    }

    /// <summary>
    /// Drops entries older than <paramref name="olderThan"/>: no backup that old remains, so no
    /// restore can need them. Called by the retention sweep after it purges backups. A backup copied
    /// off the volume is outside what this can know about, and <c>backup-and-restore.md</c> says so.
    /// </summary>
    public async Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        LedgerRead ledger = await ReadAsync(cancellationToken);

        if (!ledger.Exists)
        {
            return 0;
        }

        List<DeletionEntry> kept = [.. ledger.Entries.Where(entry => entry.DeletedAt >= olderThan)];
        int dropped = ledger.Entries.Count - kept.Count;

        if (dropped == 0)
        {
            return 0;
        }

        string temporary = PathToLedger + ".tmp";
        await File.WriteAllLinesAsync(
            temporary, kept.Select(entry => JsonSerializer.Serialize(entry)), cancellationToken);
        File.Move(temporary, PathToLedger, overwrite: true);

        return dropped;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{nameof(FileDeletionLedger)}({PathToLedger})");
}

internal sealed record DeletionEntry(Guid WorkspaceId, Guid ProjectId, DateTimeOffset DeletedAt);

internal sealed record LedgerRead(bool Exists, IReadOnlyList<DeletionEntry> Entries, int Unreadable);
