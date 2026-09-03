using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NpgsqlTypes;

namespace DevBuddy.Infrastructure.Administration;

/// <summary>
/// The serialisation options a row archive needs, shared by <see cref="BackupService"/> and
/// <see cref="ExportService"/> so the one rule — drop what PostgreSQL computes for itself — is
/// written once.
/// </summary>
internal static class ArchiveJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { DropGeneratedColumns } },
    };

    /// <summary>
    /// Leaves out the columns PostgreSQL computes for itself.
    /// <para>
    /// The full-text search vector is generated from the title and body on every write. It is not
    /// data, it is a derived index, and an archive carrying it would be storing an answer the
    /// database recomputes anyway — and could not read back, because the type it arrives as cannot
    /// be constructed from JSON at all. Reindexing after a restore is the database's job.
    /// </para>
    /// </summary>
    private static void DropGeneratedColumns(JsonTypeInfo info)
    {
        for (int index = info.Properties.Count - 1; index >= 0; index--)
        {
            if (info.Properties[index].PropertyType == typeof(NpgsqlTsVector))
            {
                info.Properties.RemoveAt(index);
            }
        }
    }
}
