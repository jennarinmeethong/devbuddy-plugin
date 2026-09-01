using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;

namespace DevBuddy.Domain.Knowledge;

/// <summary>
/// Where a revision came from, who produced it, when, and what supports it.
/// <para>
/// info.md requires provenance for every record, so this is a required constructor argument on
/// <see cref="RecordRevision"/> rather than an optional property. A revision without provenance
/// cannot be constructed, which means it cannot be persisted either.
/// </para>
/// </summary>
public sealed record Provenance
{
    public Provenance(
        ProvenanceSourceKind sourceKind,
        string sourceLocator,
        string author,
        DateTimeOffset recordedAt,
        IEnumerable<EvidenceReference>? evidence = null)
    {
        SourceKind = Guard.Defined(sourceKind, nameof(sourceKind));
        SourceLocator = Guard.NotLongerThan(
            Guard.NotBlank(sourceLocator, nameof(sourceLocator)), 1000, nameof(sourceLocator));
        Author = Guard.NotLongerThan(Guard.NotBlank(author, nameof(author)), 200, nameof(author));
        RecordedAt = Guard.Utc(recordedAt, nameof(recordedAt));
        Evidence = evidence is null ? [] : [.. evidence];
    }

    public ProvenanceSourceKind SourceKind { get; }

    /// <summary>
    /// How to find the origin again: a commit, a pull request, a document path, a tool run.
    /// Not typed as a URL, because many origins are not addressable that way.
    /// </summary>
    public string SourceLocator { get; }

    public string Author { get; }

    public DateTimeOffset RecordedAt { get; }

    public IReadOnlyList<EvidenceReference> Evidence { get; }

    /// <summary>
    /// True when the content originated from an AI draft. Kept explicit so a reviewer always
    /// knows what they are approving.
    /// </summary>
    public bool IsAiGenerated => SourceKind == ProvenanceSourceKind.AiDraft;
}
