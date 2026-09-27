using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectVisualIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color",
                schema: "projects",
                table: "projects",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "icon",
                schema: "projects",
                table: "projects",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "tags",
                schema: "projects",
                table: "projects",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "color",
                schema: "projects",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "icon",
                schema: "projects",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "tags",
                schema: "projects",
                table: "projects");
        }
    }
}
