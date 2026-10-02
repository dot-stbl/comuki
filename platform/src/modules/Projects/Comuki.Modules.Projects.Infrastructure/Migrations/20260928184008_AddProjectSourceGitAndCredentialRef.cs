using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectSourceGitAndCredentialRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_git_ref",
                schema: "projects",
                table: "projects",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_git_url",
                schema: "projects",
                table: "projects",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "git_credential_ref",
                schema: "projects",
                table: "project_settings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_git_ref",
                schema: "projects",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "source_git_url",
                schema: "projects",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "git_credential_ref",
                schema: "projects",
                table: "project_settings");
        }
    }
}
