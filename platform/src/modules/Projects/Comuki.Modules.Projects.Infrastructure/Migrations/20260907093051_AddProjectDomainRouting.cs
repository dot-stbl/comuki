using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDomainRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_domain_types_json",
                schema: "projects",
                table: "project_settings",
                type: "text",
                nullable: true);

            // domain_type defaults to "Standard" so existing rows match the
            // entity default (ProjectSettings.CreateDefaults). EF would have
            // emitted defaultValue: "" which would leave existing rows empty
            // and surface as an unknown enum value on the next read.
            migrationBuilder.AddColumn<string>(
                name: "domain_type",
                schema: "projects",
                table: "project_settings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Standard");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_domain_types_json",
                schema: "projects",
                table: "project_settings");

            migrationBuilder.DropColumn(
                name: "domain_type",
                schema: "projects",
                table: "project_settings");
        }
    }
}
