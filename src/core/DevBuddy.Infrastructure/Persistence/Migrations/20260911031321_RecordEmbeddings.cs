using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevBuddy.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The derived vector index (ADR-0012). Raw SQL and conditional, and both of those are about
    /// not breaking installations that want none of this.
    /// <para>
    /// <b>Conditional</b>, because pgvector is an extension and the image this stack ships with,
    /// <c>postgres:17-alpine</c>, does not carry it. An unguarded <c>create extension vector</c>
    /// would fail inside <c>migrate</c> — which every deployment runs and which both servers wait
    /// on — so a stack that never asked for embeddings would stop starting. The whole block is
    /// therefore skipped where the extension is unavailable, and <c>IEmbeddingIndex</c> answers
    /// that the index is absent. Embeddings are off by default; nothing else notices.
    /// </para>
    /// <para>
    /// <b>Raw SQL and not an EF entity</b>, because the table exists on only some servers. An
    /// entity in the model would make the snapshot disagree with the database on every plain
    /// PostgreSQL, which turns a supported configuration into a permanent pending-changes warning.
    /// And <c>vector</c> is not a type EF maps, so the similarity query is SQL whatever else
    /// happens here.
    /// </para>
    /// </summary>
    public partial class RecordEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The dimension is deliberately unspecified. pgvector allows a bare `vector`, and
            // committing the column to one length here would commit the installation to one
            // embedding model at migration time, before anybody has chosen one. The cost is that
            // no HNSW or IVFFlat index can be built over it, so a similarity query is an exact
            // scan bounded by the scope filter — fine for a per-project knowledge base, and stated
            // rather than discovered. An installation big enough to need approximate search picks
            // a model first and adds its own index; the model and dimension are on every row, so
            // it can see what it would be committing to.
            migrationBuilder.Sql(
                """
                do $$
                begin
                    if exists (select 1 from pg_available_extensions where name = 'vector') then
                        create extension if not exists vector;

                        create table if not exists record_embeddings (
                            record_id uuid not null,
                            revision_number integer not null,
                            model text not null,
                            workspace_id uuid not null,
                            project_id uuid not null,
                            content_hash text not null,
                            dimensions integer not null,
                            embedding vector not null,
                            embedded_at timestamptz not null,
                            constraint pk_record_embeddings
                                primary key (record_id, revision_number, model)
                        );

                        -- The scope leads the index because every query is scoped; the model is in
                        -- it because vectors from two models are not comparable and a query says
                        -- which one it means.
                        create index if not exists ix_record_embeddings_scope
                            on record_embeddings (workspace_id, project_id, model);
                    end if;
                end
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The table goes; the extension stays. Dropping an extension another database in the
            // same cluster may be using is not this migration's business, and everything lost here
            // is derived data rebuildable from the records it describes.
            migrationBuilder.Sql("drop table if exists record_embeddings;");
        }
    }
}
