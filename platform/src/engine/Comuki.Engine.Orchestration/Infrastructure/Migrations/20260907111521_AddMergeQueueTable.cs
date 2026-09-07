using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Engine.Orchestration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMergeQueueTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "merge_queue",
                schema: "orchestration",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    pull_request_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    conflict_resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    claimed_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    abandoned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    abandoned_reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    notes = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_merge_queue", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_merge_queue_project_id",
                schema: "orchestration",
                table: "merge_queue",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_merge_queue_status_enqueued_at",
                schema: "orchestration",
                table: "merge_queue",
                columns: ["status", "enqueued_at"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "merge_queue",
                schema: "orchestration");
        }
    }
}
