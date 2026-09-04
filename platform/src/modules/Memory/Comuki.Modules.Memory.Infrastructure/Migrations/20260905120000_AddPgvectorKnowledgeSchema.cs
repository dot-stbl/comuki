using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Memory.Infrastructure.Migrations
{
    /// <summary>
    /// Adds the knowledge-base pgvector schema to the memory module:
    /// <c>source_documents</c> (git | upload | url pointers) and
    /// <c>memory_embeddings</c> (chunks + optional pgvector vector(1536)
    /// column). Migration is hand-authored (the
    /// <c>ef-migrations.md</c> recovery procedure applies if a future
    /// tool run regenerates a divergent migration): the pgvector column
    /// is conditionally created in raw SQL so the migration stays
    /// graceful on a vanilla postgres image (see
    /// <see cref="InitialMemorySchema"/> for the same pattern on
    /// <c>memory_facts.embedding</c>).
    /// </summary>
    public partial class AddPgvectorKnowledgeSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "memory");

            migrationBuilder.CreateTable(
                name: "source_documents",
                schema: "memory",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_ref = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_source_documents", static x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "memory_embeddings",
                schema: "memory",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    chunk_text = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_memory_embeddings", static x => x.id);
                    table.ForeignKey(
                        name: "fk_memory_embeddings_source_documents_source_document_id",
                        column: static x => x.source_document_id,
                        principalSchema: "memory",
                        principalTable: "source_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_source_documents_project_created",
                schema: "memory",
                table: "source_documents",
                columns: ["project_id", "created_at"]);

            migrationBuilder.CreateIndex(
                name: "ix_memory_embeddings_source_chunk",
                schema: "memory",
                table: "memory_embeddings",
                columns: ["source_document_id", "chunk_index"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memory_embeddings_project",
                schema: "memory",
                table: "memory_embeddings",
                column: "project_id");

            // pgvector extension + vector(1536) column + ivfflat cosine index.
            // Degrades gracefully: on a plain postgres image (no pgvector
            // available) the notice is raised and the embedding column +
            // ivfflat index are skipped — knowledge still works via the
            // fallback ranking path.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    CREATE EXTENSION IF NOT EXISTS vector;
                EXCEPTION
                    WHEN OTHERS THEN
                        RAISE NOTICE 'pgvector extension unavailable; memory_embeddings.embedding skipped';
                END
                $$;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'vector') THEN
                        ALTER TABLE memory.memory_embeddings ADD COLUMN embedding vector(1536);
                        CREATE INDEX ix_memory_embeddings_embedding_ivfflat
                            ON memory.memory_embeddings
                            USING ivfflat (embedding vector_cosine_ops)
                            WITH (lists = 100);
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop ivfflat index + embedding column first (raw SQL, conditional).
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'memory'
                                 AND table_name = 'memory_embeddings'
                                 AND column_name = 'embedding') THEN
                        DROP INDEX IF EXISTS memory.ix_memory_embeddings_embedding_ivfflat;
                        ALTER TABLE memory.memory_embeddings DROP COLUMN embedding;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropTable(
                name: "memory_embeddings",
                schema: "memory");

            migrationBuilder.DropTable(
                name: "source_documents",
                schema: "memory");
        }
    }
}
