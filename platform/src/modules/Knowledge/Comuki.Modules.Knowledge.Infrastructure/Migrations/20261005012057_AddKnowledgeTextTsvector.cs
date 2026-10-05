using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Knowledge.Infrastructure.Migrations
{
    /// <summary>
    /// Adds generated <c>tsvector</c> columns and GIN indexes on the
    /// chunkable text fields of the knowledge corpus
    /// (<c>knowledge.memory_embeddings.chunk_text</c> and
    /// <c>knowledge.source_documents.title</c>). Hand-authored alongside
    /// <c>20260907012811_MoveKnowledgeEmbeddingsToKnowledgeSchema</c> —
    /// the EF model does not see the columns (same separation as the
    /// pgvector <c>embedding</c> column), so the generator produced an
    /// empty body. The <c>simple</c> text-search configuration is the
    /// language-agnostic choice; the columns are generated on every
    /// write so the SQL path can rank by lexeme overlap without
    /// re-running <c>to_tsvector</c> per search. Postgres has shipped
    /// <c>tsvector</c> + GIN since 8.3 so no graceful skip is needed.
    /// </summary>
    public partial class AddKnowledgeTextTsvector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE knowledge.source_documents
                ADD COLUMN text_tsv tsvector
                GENERATED ALWAYS AS (to_tsvector('simple', coalesce(title, ''))) STORED;

                CREATE INDEX ix_source_documents_title_tsv
                ON knowledge.source_documents
                USING GIN (text_tsv);

                ALTER TABLE knowledge.memory_embeddings
                ADD COLUMN text_tsv tsvector
                GENERATED ALWAYS AS (to_tsvector('simple', coalesce(chunk_text, ''))) STORED;

                CREATE INDEX ix_memory_embeddings_text_tsv
                ON knowledge.memory_embeddings
                USING GIN (text_tsv);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS knowledge.ix_memory_embeddings_text_tsv;
                DROP INDEX IF EXISTS knowledge.ix_source_documents_title_tsv;
                ALTER TABLE knowledge.memory_embeddings DROP COLUMN IF EXISTS text_tsv;
                ALTER TABLE knowledge.source_documents DROP COLUMN IF EXISTS text_tsv;
                """);
        }
    }
}
