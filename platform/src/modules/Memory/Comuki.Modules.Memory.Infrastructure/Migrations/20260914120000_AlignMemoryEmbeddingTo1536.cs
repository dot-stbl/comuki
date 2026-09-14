using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Memory.Infrastructure.Migrations
{
    /// <summary>
    /// Aligns <c>memory_facts.embedding</c> with the deployed embedding
    /// model — the same <c>text-embedding-3-small</c> / 1536-dim provider
    /// the knowledge schema already pins (<c>KnowledgeEmbeddingOptions</c>
    /// default, <c>knowledge.memory_embeddings.embedding vector(1536)</c>).
    /// One embedding model serves both stores, so the 768-dim column the
    /// initial schema created is replaced: dimension changes never migrate
    /// vectors (see <c>MemoryFactPolicy.EmbeddingDimensions</c>) — the
    /// column is dropped and recreated empty; facts were never embedded
    /// through any caller, so nothing is lost, and search keeps working
    /// via the fallback ranking while the re-embedding happens naturally
    /// on the next write. Also adds the missing ivfflat ANN index
    /// (vector_cosine_ops, lists = 50 — a small table wants few lists).
    /// Hand-authored raw SQL per the house pgvector pattern (the column
    /// lives outside the EF model): on a plain postgres image without
    /// pgvector the whole block no-ops, exactly like the initial schema.
    /// </summary>
    public partial class AlignMemoryEmbeddingTo1536 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'vector') THEN
                        IF EXISTS (SELECT 1 FROM information_schema.columns
                                   WHERE table_schema = 'memory'
                                     AND table_name = 'memory_facts'
                                     AND column_name = 'embedding') THEN
                            DROP INDEX IF EXISTS memory.ix_memory_facts_embedding_ivfflat;
                            ALTER TABLE memory.memory_facts DROP COLUMN embedding;
                        END IF;
                        ALTER TABLE memory.memory_facts ADD COLUMN embedding vector(1536);
                        CREATE INDEX ix_memory_facts_embedding_ivfflat
                            ON memory.memory_facts
                            USING ivfflat (embedding vector_cosine_ops)
                            WITH (lists = 50);
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'memory'
                                 AND table_name = 'memory_facts'
                                 AND column_name = 'embedding') THEN
                        DROP INDEX IF EXISTS memory.ix_memory_facts_embedding_ivfflat;
                        ALTER TABLE memory.memory_facts DROP COLUMN embedding;
                        ALTER TABLE memory.memory_facts ADD COLUMN embedding vector(768);
                    END IF;
                END
                $$;
                """);
        }
    }
}
