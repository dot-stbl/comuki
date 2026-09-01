using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Chat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialChatSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_checkpoints",
                columns: static table => new
                {
                    thread_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    step = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot = table.Column<string>(type: "text", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_chat_checkpoints", static x => new { x.thread_id, x.step });
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    tool_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_chat_messages", static x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_sessions",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_chat_sessions", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_checkpoints_thread_id",
                table: "chat_checkpoints",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_session_id",
                table: "chat_messages",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_sessions_subject_id",
                table: "chat_sessions",
                column: "subject_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_checkpoints");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "chat_sessions");
        }
    }
}
