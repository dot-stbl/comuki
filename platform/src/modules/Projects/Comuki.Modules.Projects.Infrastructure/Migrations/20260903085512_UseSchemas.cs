using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "projects");

            migrationBuilder.RenameTable(
                name: "projects",
                newName: "projects",
                newSchema: "projects");

            migrationBuilder.RenameTable(
                name: "project_settings",
                newName: "project_settings",
                newSchema: "projects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "projects",
                schema: "projects",
                newName: "projects");

            migrationBuilder.RenameTable(
                name: "project_settings",
                schema: "projects",
                newName: "project_settings");
        }
    }
}
