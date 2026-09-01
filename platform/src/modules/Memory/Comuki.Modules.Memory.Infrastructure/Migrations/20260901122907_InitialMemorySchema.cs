using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Memory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMemorySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_checkpoints",
                columns: static table => new
                {
                    session_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    graph_state = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_chat_checkpoints", static x => x.session_id);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_chat_messages", static x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "learning_candidates",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pattern = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    source_ref = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    repeat_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_learning_candidates", static x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "memory_facts",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    topic_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_memory_facts", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_session_id",
                table: "chat_messages",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_learning_candidates_status",
                table: "learning_candidates",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_memory_facts_active_topic",
                table: "memory_facts",
                columns: ["scope", "subject_id", "topic_key"],
                unique: true,
                filter: "superseded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_memory_facts_subject_created",
                table: "memory_facts",
                columns: ["scope", "subject_id", "created_at"]);

            // Custom SQL beyond what EF can express (the documented exception):
            // CREATE EXTENSION and the vector(768) column type need pgvector,
            // which is deliberately outside the EF model (raw-SQL managed).
            // Degrades gracefully: on a plain postgres image (no pgvector
            // available) the notice is raised and the embedding column is
            // skipped — memory keeps working via the fallback ranking
            // (add-chat-memory contract).
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    CREATE EXTENSION IF NOT EXISTS vector;
                EXCEPTION
                    WHEN OTHERS THEN
                        RAISE NOTICE 'pgvector extension unavailable; memory_facts.embedding skipped';
                END
                $$;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'vector') THEN
                        ALTER TABLE memory_facts ADD COLUMN embedding vector(768);
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_checkpoints");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "learning_candidates");

            migrationBuilder.DropTable(
                name: "memory_facts");
        }
    }
}
