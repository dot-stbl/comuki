using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Artifacts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVisualArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "visual_artifacts",
                schema: "artifacts",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    filename = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_visual_artifacts", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_visual_artifacts_project_run",
                schema: "artifacts",
                table: "visual_artifacts",
                columns: ["project_id", "run_id"]);

            migrationBuilder.CreateIndex(
                name: "ix_visual_artifacts_project_session",
                schema: "artifacts",
                table: "visual_artifacts",
                columns: ["project_id", "session_id"]);

            migrationBuilder.CreateIndex(
                name: "ix_visual_artifacts_project_work_item",
                schema: "artifacts",
                table: "visual_artifacts",
                columns: ["project_id", "work_item_id"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visual_artifacts",
                schema: "artifacts");
        }
    }
}
