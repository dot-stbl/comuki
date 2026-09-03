using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Engine.Orchestration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orchestration");

            migrationBuilder.RenameTable(
                name: "work_items",
                newName: "work_items",
                newSchema: "orchestration");

            migrationBuilder.RenameTable(
                name: "work_item_dependencies",
                newName: "work_item_dependencies",
                newSchema: "orchestration");

            migrationBuilder.RenameTable(
                name: "runs",
                newName: "runs",
                newSchema: "orchestration");

            migrationBuilder.RenameTable(
                name: "run_events",
                newName: "run_events",
                newSchema: "orchestration");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "work_items",
                schema: "orchestration",
                newName: "work_items");

            migrationBuilder.RenameTable(
                name: "work_item_dependencies",
                schema: "orchestration",
                newName: "work_item_dependencies");

            migrationBuilder.RenameTable(
                name: "runs",
                schema: "orchestration",
                newName: "runs");

            migrationBuilder.RenameTable(
                name: "run_events",
                schema: "orchestration",
                newName: "run_events");
        }
    }
}
