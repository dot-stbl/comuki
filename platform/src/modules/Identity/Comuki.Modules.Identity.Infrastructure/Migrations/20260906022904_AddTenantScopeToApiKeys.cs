using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantScopeToApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_project_id",
                schema: "identity",
                table: "api_keys",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_tenant_project_id",
                schema: "identity",
                table: "api_keys",
                column: "tenant_project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_api_keys_tenant_project_id",
                schema: "identity",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "tenant_project_id",
                schema: "identity",
                table: "api_keys");
        }
    }
}
