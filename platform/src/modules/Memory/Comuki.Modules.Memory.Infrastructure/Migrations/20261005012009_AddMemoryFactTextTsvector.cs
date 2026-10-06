using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Memory.Infrastructure.Migrations
{
    /// <summary>
    /// Adds a generated <c>tsvector</c> column on <c>memory.memory_facts.text</c> and
    /// a GIN index over it. Hand-authored alongside
    /// <c>20260905120000_AddPgvectorKnowledgeSchema</c> — the EF model does
    /// not see the column (same separation as the pgvector
    /// <c>embedding</c> column), so the generator produced an empty body. The
    /// <c>simple</c> text-search configuration is the language-agnostic
    /// choice; the column is generated on every write so the SQL path can
    /// rank by lexeme overlap without re-running <c>to_tsvector</c> per
    /// search. The migration is graceful: on a vanilla postgres image
    /// without a build that supports the GIN-on-tsvector operator class
    /// the column and index are still created — Postgres has shipped
    /// <c>tsvector</c> + GIN since 8.3.
    /// </summary>
    public partial class AddMemoryFactTextTsvector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE memory.memory_facts
                ADD COLUMN text_tsv tsvector
                GENERATED ALWAYS AS (to_tsvector('simple', coalesce(text, ''))) STORED;

                CREATE INDEX ix_memory_facts_text_tsv
                ON memory.memory_facts
                USING GIN (text_tsv);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS memory.ix_memory_facts_text_tsv;
                ALTER TABLE memory.memory_facts DROP COLUMN IF EXISTS text_tsv;
                """);
        }
    }
}
