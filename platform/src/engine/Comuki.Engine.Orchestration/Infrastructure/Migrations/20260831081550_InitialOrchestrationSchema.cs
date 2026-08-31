using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Engine.Orchestration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrchestrationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.CreateTable(
                name: "runs",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    _ = table.PrimaryKey("pk_runs", static x => x.id);
                });

            _ = migrationBuilder.CreateTable(
                name: "run_events",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    _ = table.PrimaryKey("pk_run_events", static x => x.id);
                    _ = table.ForeignKey(
                        name: "fk_run_events_runs_run_id",
                        column: static x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            _ = migrationBuilder.CreateTable(
                name: "work_items",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    profile_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    brief = table.Column<string>(type: "jsonb", nullable: false),
                    leased_by = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    _ = table.PrimaryKey("pk_work_items", static x => x.id);
                    _ = table.ForeignKey(
                        name: "fk_work_items_runs_run_id",
                        column: static x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            _ = migrationBuilder.CreateTable(
                name: "work_item_dependencies",
                columns: static table => new
                {
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depends_on_work_item_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: static table =>
                {
                    _ = table.PrimaryKey("pk_work_item_dependencies", static x => new { x.work_item_id, x.depends_on_work_item_id });
                    _ = table.ForeignKey(
                        name: "fk_work_item_dependencies_work_items_depends_on_work_item_id",
                        column: static x => x.depends_on_work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    _ = table.ForeignKey(
                        name: "fk_work_item_dependencies_work_items_work_item_id",
                        column: static x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            _ = migrationBuilder.CreateIndex(
                name: "ix_run_events_run_id_occurred_at",
                table: "run_events",
                columns: ["run_id", "occurred_at"]);

            _ = migrationBuilder.CreateIndex(
                name: "ix_work_item_dependencies_depends_on_work_item_id",
                table: "work_item_dependencies",
                column: "depends_on_work_item_id");

            _ = migrationBuilder.CreateIndex(
                name: "ix_work_items_active",
                table: "work_items",
                columns: ["status", "created_at"],
                filter: "status IN ('queued', 'running')");

            _ = migrationBuilder.CreateIndex(
                name: "ix_work_items_run_id",
                table: "work_items",
                column: "run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.DropTable(
                name: "run_events");

            _ = migrationBuilder.DropTable(
                name: "work_item_dependencies");

            _ = migrationBuilder.DropTable(
                name: "work_items");

            _ = migrationBuilder.DropTable(
                name: "runs");
        }
    }
}
