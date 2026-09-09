using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Engine.Orchestration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RunIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_runs_project_id_status",
                schema: "orchestration",
                table: "runs",
                columns: ["project_id", "status"]);

            migrationBuilder.CreateIndex(
                name: "ix_runs_status_updated_at",
                schema: "orchestration",
                table: "runs",
                columns: ["status", "updated_at"]);

            migrationBuilder.CreateIndex(
                name: "ix_runs_updated_at",
                schema: "orchestration",
                table: "runs",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_runs_project_id_status",
                schema: "orchestration",
                table: "runs");

            migrationBuilder.DropIndex(
                name: "ix_runs_status_updated_at",
                schema: "orchestration",
                table: "runs");

            migrationBuilder.DropIndex(
                name: "ix_runs_updated_at",
                schema: "orchestration",
                table: "runs");
        }
    }
}
