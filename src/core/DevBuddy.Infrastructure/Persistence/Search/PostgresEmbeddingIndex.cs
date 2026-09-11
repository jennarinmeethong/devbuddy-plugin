using System.Globalization;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace DevBuddy.Infrastructure.Persistence.Search;

/// <summary>
/// The derived vector index, in PostgreSQL with pgvector (ADR-0012).
/// <para>
/// Raw SQL throughout, and not because EF was inconvenient: the table exists on only some servers
/// (see the <c>RecordEmbeddings</c> migration), and <c>vector</c> is not a type EF maps. A
/// similarity query is <c>order by embedding &lt;=&gt; $1</c> and there is no LINQ spelling of it.
/// </para>
/// <para>
/// <b>Every statement carries the scope in its <c>where</c> clause.</b> Not as a belt beside the
/// pipeline's braces — as the only isolation there is here. This table has no EF global query
/// filter, because it has no EF entity, so the filter that protects every other table protects
/// nothing in this one. ADR-0012 says isolation belongs in the query, and this is the class that
/// sentence was written about.
/// </para>
/// <para>
/// Parameters everywhere, including the vector. A vector rendered into the statement as text would
/// be the one place in this system where a float array became SQL, and the reason to avoid that is
/// not injection — 1,536 floats are hard to weaponise — but that it teaches the pattern.
/// </para>
/// </summary>
internal sealed class PostgresEmbeddingIndex(DevBuddyDbContext db) : IEmbeddingIndex
{
    /// <summary>
    /// The largest result set a similarity query will return, whatever it was asked for. A
    /// nearest-neighbour query with no ceiling is a full table scan with a sort, and the caller
    /// that wants "everything, ranked" wants full-text search instead (SB-25).
    /// </summary>
    private const int MaximumLimit = 200;

    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = Command(
            """
            select exists (
                select 1 from information_schema.tables
                where table_name = 'record_embeddings'
            );
            """);

        await OpenAsync(command, cancellationToken);
        object? answer = await command.ExecuteScalarAsync(cancellationToken);

        return answer is true;
    }

    public async Task<int> UpsertAsync(
        ProjectScope scope,
        string model,
        IReadOnlyList<EmbeddedRevision> entries,
        CancellationToken cancellationToken)
    {
        Guard.NotBlank(model, nameof(model));
        Guard.NotNull(entries, nameof(entries));

        if (entries.Count == 0)
        {
            return 0;
        }

        int written = 0;

        // One statement per entry rather than one multi-row insert. A batch would be faster and
        // would also mean building a parameter list whose length varies with the input, which is
        // the shape that eventually becomes string concatenation. Embedding is a background job
        // measured in provider latency; the round trips are not the cost here.
        foreach (EmbeddedRevision entry in entries)
        {
            await using NpgsqlCommand command = Command(
                """
                insert into record_embeddings (
                    record_id, revision_number, model, workspace_id, project_id,
                    content_hash, dimensions, embedding, embedded_at)
                values ($1, $2, $3, $4, $5, $6, $7, $8, now())
                on conflict (record_id, revision_number, model) do update set
                    content_hash = excluded.content_hash,
                    dimensions = excluded.dimensions,
                    embedding = excluded.embedding,
                    embedded_at = excluded.embedded_at;
                """,
                Uuid(entry.RecordId.Value),
                Integer(entry.RevisionNumber),
                Text(model),
                Uuid(scope.WorkspaceId.Value),
                Uuid(scope.ProjectId.Value),
                Text(entry.ContentHash),
                Integer(entry.Vector.Length),
                Vector(entry.Vector));

            await OpenAsync(command, cancellationToken);
            written += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return written;
    }

    public async Task<IReadOnlyList<SimilarRevision>> FindSimilarAsync(
        ProjectScope scope,
        string model,
        ReadOnlyMemory<float> query,
        int limit,
        CancellationToken cancellationToken)
    {
        Guard.NotBlank(model, nameof(model));

        if (query.Length == 0)
        {
            // An empty vector has no nearest neighbour, and pgvector would refuse the comparison.
            // Answered as no hits rather than as an error: the caller asked a question with no
            // content in it.
            return [];
        }

        // Dimensions are matched as well as the model. Two models sharing a name across an
        // upgrade is the case this catches — pgvector raises on a dimension mismatch, and an
        // exception from a background sweep is a worse answer than skipping rows that cannot be
        // compared.
        await using NpgsqlCommand command = Command(
            """
            select record_id, revision_number, embedding <=> $1 as distance
            from record_embeddings
            where workspace_id = $2
              and project_id = $3
              and model = $4
              and dimensions = $5
            order by embedding <=> $1
            limit $6;
            """,
            Vector(query),
            Uuid(scope.WorkspaceId.Value),
            Uuid(scope.ProjectId.Value),
            Text(model),
            Integer(query.Length),
            Integer(Math.Clamp(limit, 1, MaximumLimit)));

        await OpenAsync(command, cancellationToken);

        List<SimilarRevision> hits = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            hits.Add(new SimilarRevision(
                new KnowledgeRecordId(reader.GetGuid(0)),
                reader.GetInt32(1),
                reader.GetDouble(2)));
        }

        return hits;
    }

    public async Task<IReadOnlySet<string>> IndexedContentHashesAsync(
        ProjectScope scope, string model, CancellationToken cancellationToken)
    {
        Guard.NotBlank(model, nameof(model));

        await using NpgsqlCommand command = Command(
            """
            select content_hash
            from record_embeddings
            where workspace_id = $1 and project_id = $2 and model = $3;
            """,
            Uuid(scope.WorkspaceId.Value),
            Uuid(scope.ProjectId.Value),
            Text(model));

        await OpenAsync(command, cancellationToken);

        HashSet<string> hashes = new(StringComparer.Ordinal);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            hashes.Add(reader.GetString(0));
        }

        return hashes;
    }

    public async Task<int> PurgeProjectAsync(ProjectScope scope, CancellationToken cancellationToken)
    {
        // Guarded by the table's existence rather than by the caller remembering to ask: this is
        // called from delete_project, which runs on every installation whether or not it has
        // pgvector.
        if (!await IsAvailableAsync(cancellationToken))
        {
            return 0;
        }

        await using NpgsqlCommand command = Command(
            """
            delete from record_embeddings
            where workspace_id = $1 and project_id = $2;
            """,
            Uuid(scope.WorkspaceId.Value),
            Uuid(scope.ProjectId.Value));

        await OpenAsync(command, cancellationToken);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private NpgsqlCommand Command(string sql, params NpgsqlParameter[] parameters)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (NpgsqlParameter parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static async Task OpenAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync(cancellationToken);
        }
    }

    private static NpgsqlParameter Uuid(Guid value) =>
        new() { NpgsqlDbType = NpgsqlDbType.Uuid, Value = value };

    private static NpgsqlParameter Integer(int value) =>
        new() { NpgsqlDbType = NpgsqlDbType.Integer, Value = value };

    private static NpgsqlParameter Text(string value) =>
        new() { NpgsqlDbType = NpgsqlDbType.Text, Value = value };

    /// <summary>
    /// A vector, as the literal pgvector accepts, passed as an untyped parameter so the server
    /// casts it.
    /// <para>
    /// Npgsql has no built-in mapping for <c>vector</c> — that is what the <c>Pgvector</c> package
    /// adds — and taking a package dependency to format a comma-separated list is not a trade
    /// worth making for a type this codebase touches in one file. Invariant culture, because a
    /// decimal comma would turn one vector into twice as many numbers.
    /// </para>
    /// </summary>
    private static NpgsqlParameter Vector(ReadOnlyMemory<float> value) =>
        new()
        {
            NpgsqlDbType = NpgsqlDbType.Unknown,
            Value = "[" + string.Join(
                ',',
                value.ToArray().Select(number => number.ToString("R", CultureInfo.InvariantCulture)))
                + "]",
        };
}
