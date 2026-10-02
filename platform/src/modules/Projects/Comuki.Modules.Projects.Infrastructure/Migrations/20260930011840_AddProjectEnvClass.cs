using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectEnvClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "env_class",
                schema: "projects",
                table: "projects",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "env_class",
                schema: "projects",
                table: "projects");
        }
    }
}
