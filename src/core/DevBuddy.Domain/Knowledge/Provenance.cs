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
        // Typed as the property it fills rather than as the loosest thing that would work. A
        // constructor parameter whose type differs from the property it names cannot be bound by
        // name, which breaks every by-name construction: serialisers, mappers, and records.
        IReadOnlyList<EvidenceReference>? evidence = null,
        bool isAiGenerated = false)
    {
        SourceKind = Guard.Defined(sourceKind, nameof(sourceKind));
        SourceLocator = Guard.NotLongerThan(
            Guard.NotBlank(sourceLocator, nameof(sourceLocator)), 1000, nameof(sourceLocator));
        Author = Guard.NotLongerThan(Guard.NotBlank(author, nameof(author)), 200, nameof(author));
        RecordedAt = Guard.Utc(recordedAt, nameof(recordedAt));
        Evidence = evidence is null ? [] : [.. evidence];

        // A declared AI draft is AI-generated whatever else is said. The reverse never holds: a
        // source kind other than AiDraft says what the content was drawn from, not who wrote it.
        IsAiGenerated = isAiGenerated || SourceKind == ProvenanceSourceKind.AiDraft;
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
    /// True when an AI wrote the content. Kept explicit so a reviewer always knows what they are
    /// approving.
    /// <para>
    /// Stored, not computed from <see cref="SourceKind"/>. Until 2026-09-15 it was computed, so a
    /// draft written over the AI channel that named <c>RepositoryAnalysis</c> as its source was
    /// recorded, reviewed, and published as written by a person. Whoever writes the content cannot
    /// be the one who decides this: the application sets it from the channel the draft arrived
    /// on, and a caller can only ever add it, by declaring <see cref="ProvenanceSourceKind.AiDraft"/>.
    /// </para>
    /// </summary>
    public bool IsAiGenerated { get; }

    /// <summary>The same provenance, marked as written by an AI.</summary>
    public Provenance AsAiGenerated() =>
        IsAiGenerated ? this : new(SourceKind, SourceLocator, Author, RecordedAt, Evidence, isAiGenerated: true);
}
