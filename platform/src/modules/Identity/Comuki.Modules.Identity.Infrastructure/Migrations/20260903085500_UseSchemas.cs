using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.RenameTable(
                name: "users",
                newName: "users",
                newSchema: "identity");

            migrationBuilder.RenameTable(
                name: "role_assignments",
                newName: "role_assignments",
                newSchema: "identity");

            migrationBuilder.RenameTable(
                name: "oidc_links",
                newName: "oidc_links",
                newSchema: "identity");

            migrationBuilder.RenameTable(
                name: "api_keys",
                newName: "api_keys",
                newSchema: "identity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "users",
                schema: "identity",
                newName: "users");

            migrationBuilder.RenameTable(
                name: "role_assignments",
                schema: "identity",
                newName: "role_assignments");

            migrationBuilder.RenameTable(
                name: "oidc_links",
                schema: "identity",
                newName: "oidc_links");

            migrationBuilder.RenameTable(
                name: "api_keys",
                schema: "identity",
                newName: "api_keys");
        }
    }
}
