using System.Text;
using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Knowledge;

/// <summary>
/// One immutable version of a record. Nothing here changes after construction: a correction
/// produces a new revision, it never rewrites this one. That is control SB-24, and it is what
/// makes an approval bound to a content hash meaningful.
/// <para>
/// Structured front matter and the Markdown body are held together, because info.md requires
/// human-readable content linked to its database record rather than stored apart from it.
/// </para>
/// </summary>
public sealed class RecordRevision
{
    public RecordRevision(
        int number,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? frontMatter,
        Provenance provenance,
        DateTimeOffset createdAt,
        UserId createdBy)
    {
        if (number < 1)
        {
            throw new DomainValidationException("Revision numbers start at 1.");
        }

        Number = number;
        Title = Guard.NotLongerThan(Guard.NotBlank(title, nameof(title)), 500, nameof(title));
        Body = Guard.NotNull(body, nameof(body));
        FrontMatter = frontMatter is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(frontMatter, StringComparer.Ordinal);
        Provenance = Guard.NotNull(provenance, nameof(provenance));
        CreatedAt = Guard.Utc(createdAt, nameof(createdAt));
        CreatedBy = createdBy;
        ContentHash = ContentHash.FromContent(BuildCanonicalContent(Title, FrontMatter, Body));
    }

    public int Number { get; }

    public string Title { get; }

    /// <summary>The Markdown body, exactly as a reviewer reads it.</summary>
    public string Body { get; }

    public IReadOnlyDictionary<string, string> FrontMatter { get; }

    public Provenance Provenance { get; }

    public DateTimeOffset CreatedAt { get; }

    public UserId CreatedBy { get; }

    /// <summary>
    /// Hash of the canonical content, computed at construction from the title, front matter,
    /// and body, so it always describes what this revision actually contains rather than what
    /// a caller claimed it contains.
    /// </summary>
    public ContentHash ContentHash { get; }

    /// <summary>
    /// The exact text the hash covers. Deterministic: front matter keys are ordered, so the
    /// same content always produces the same hash regardless of insertion order.
    /// </summary>
    private static string BuildCanonicalContent(
        string title,
        IReadOnlyDictionary<string, string> frontMatter,
        string body)
    {
        var builder = new StringBuilder();
        builder.Append("title: ").Append(title).Append('\n');

        foreach (KeyValuePair<string, string> entry in frontMatter.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            builder.Append(entry.Key).Append(": ").Append(entry.Value).Append('\n');
        }

        builder.Append("---\n").Append(body);
        return builder.ToString();
    }
}
