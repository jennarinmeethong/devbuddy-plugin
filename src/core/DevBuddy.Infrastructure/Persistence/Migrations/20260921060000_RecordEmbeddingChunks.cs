using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevBuddy.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// One row per chunk of a record's text rather than one per record (Phase 13, D9).
    /// <para>
    /// Conditional, like the migration that created the table: it runs only where
    /// <c>record_embeddings</c> exists, which is only where pgvector does. Every row already there
    /// becomes chunk 0, which is what it was: the start of the record, and on a self-hosted model
    /// with a short context, only the start.
    /// </para>
    /// </summary>
    public partial class RecordEmbeddingChunks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                do $$
                begin
                    if to_regclass('public.record_embeddings') is not null then
                        alter table record_embeddings
                            add column if not exists chunk integer not null default 0;

                        alter table record_embeddings drop constraint if exists pk_record_embeddings;

                        alter table record_embeddings add constraint pk_record_embeddings
                            primary key (record_id, revision_number, model, chunk);
                    end if;
                end
                $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                do $$
                begin
                    if to_regclass('public.record_embeddings') is not null then
                        delete from record_embeddings where chunk <> 0;
                        alter table record_embeddings drop constraint if exists pk_record_embeddings;
                        alter table record_embeddings add constraint pk_record_embeddings
                            primary key (record_id, revision_number, model);
                        alter table record_embeddings drop column if exists chunk;
                    end if;
                end
                $$;
                """);
        }
    }
}
