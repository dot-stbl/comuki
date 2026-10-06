using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Verify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGenericCommandRunWorkItemId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "work_item_id",
                schema: "verify",
                table: "generic_command_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_generic_command_runs_project_work_item",
                schema: "verify",
                table: "generic_command_runs",
                columns: new[] { "project_id", "work_item_id" },
                filter: "work_item_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_generic_command_runs_project_work_item",
                schema: "verify",
                table: "generic_command_runs");

            migrationBuilder.DropColumn(
                name: "work_item_id",
                schema: "verify",
                table: "generic_command_runs");
        }
    }
}
