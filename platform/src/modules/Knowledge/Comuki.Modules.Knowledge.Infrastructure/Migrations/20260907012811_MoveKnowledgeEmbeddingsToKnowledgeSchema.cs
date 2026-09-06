using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Knowledge.Infrastructure.Migrations
{
    /// <summary>
    /// Moves the knowledge-base tables from the <c>memory</c> schema to
    /// their own <c>knowledge</c> schema. Same column shape, same indexes,
    /// same conditional pgvector <c>embedding</c> column on
    /// <c>memory_embeddings</c> — the only difference is the schema
    /// namespace, which lets the knowledge module own its tables without
    /// sharing the <see cref="Modules.Memory.Infrastructure.Persistence.MemoryDbContext"/>.
    ///
    /// The migration is hand-authored (see <c>ef-migrations.md</c>): the
    /// EF tool would generate a half of the move at a time, but the move
    /// is conceptually atomic — drop the old physical tables and create
    /// the new ones in one transaction. The pgvector column is
    /// conditionally re-added in raw SQL so the migration stays graceful
    /// on a vanilla postgres image (same pattern as
    /// <c>AddPgvectorKnowledgeSchema</c> in the memory module).
    ///
    /// If a future <c>dotnet ef migrations add</c> on
    /// <see cref="Comuki.Modules.Knowledge.Infrastructure.Persistence.KnowledgeDbContext"/>
    /// regenerates a divergent migration, follow the
    /// <c>ef-migrations.md</c> recovery procedure: re-author this file
    /// or drop the new one and re-add with the desired name.
    /// </summary>
    public partial class MoveKnowledgeEmbeddingsToKnowledgeSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- Drop the old physical tables from `memory` ----------
            //
            // The pgvector ivfflat index references the embedding column —
            // drop it before the column, then drop the table. We do this
            // before the new knowledge.* tables are created so the move is
            // reversible: if the migration fails on the new tables, the
            // old ones are still present and the schema is unchanged.
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

                DROP TABLE IF EXISTS memory.memory_embeddings;
                DROP TABLE IF EXISTS memory.source_documents;
                """);

            // ---------- Create the new tables in `knowledge` ----------
            migrationBuilder.EnsureSchema(
                name: "knowledge");

            migrationBuilder.CreateTable(
                name: "source_documents",
                schema: "knowledge",
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
                schema: "knowledge",
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
                        principalSchema: "knowledge",
                        principalTable: "source_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_source_documents_project_created",
                schema: "knowledge",
                table: "source_documents",
                columns: ["project_id", "created_at"]);

            migrationBuilder.CreateIndex(
                name: "ix_memory_embeddings_source_chunk",
                schema: "knowledge",
                table: "memory_embeddings",
                columns: ["source_document_id", "chunk_index"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memory_embeddings_project",
                schema: "knowledge",
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
                        RAISE NOTICE 'pgvector extension unavailable; knowledge.memory_embeddings.embedding skipped';
                END
                $$;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'vector') THEN
                        ALTER TABLE knowledge.memory_embeddings ADD COLUMN embedding vector(1536);
                        CREATE INDEX ix_memory_embeddings_embedding_ivfflat
                            ON knowledge.memory_embeddings
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
                               WHERE table_schema = 'knowledge'
                                 AND table_name = 'memory_embeddings'
                                 AND column_name = 'embedding') THEN
                        DROP INDEX IF EXISTS knowledge.ix_memory_embeddings_embedding_ivfflat;
                        ALTER TABLE knowledge.memory_embeddings DROP COLUMN embedding;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropTable(
                name: "memory_embeddings",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "source_documents",
                schema: "knowledge");
        }
    }
}
