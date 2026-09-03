using DevBuddy.Infrastructure.Persistence;

namespace DevBuddy.Infrastructure.Administration;

/// <summary>
/// Everything one project's export holds. The project-scoped sibling of
/// <see cref="BackupArchive"/> — same shape, same reasoning (ADR-0011), narrowed to one project's
/// rows rather than the whole installation.
/// </summary>
internal sealed class ExportArchive
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid ProjectId { get; set; }

    public List<WorkItemRow> WorkItems { get; set; } = [];

    public List<KnowledgeRecordRow> KnowledgeRecords { get; set; } = [];

    public List<EvidenceObjectRow> EvidenceObjects { get; set; } = [];
}
