using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Engine.Orchestration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxInboxTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_receipts",
                schema: "orchestration",
                columns: static table => new
                {
                    message_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_inbox_receipts", static x => x.message_id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "orchestration",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    dead_lettered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_outbox_messages", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_undispatched",
                schema: "orchestration",
                table: "outbox_messages",
                columns: ["dispatched_at", "created_at"],
                filter: "dispatched_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_receipts",
                schema: "orchestration");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "orchestration");
        }
    }
}
