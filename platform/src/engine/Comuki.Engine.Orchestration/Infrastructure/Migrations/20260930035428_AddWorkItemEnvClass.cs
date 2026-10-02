using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Engine.Orchestration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemEnvClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_work_items_claim",
                schema: "orchestration",
                table: "work_items");

            migrationBuilder.AddColumn<string>(
                name: "env_class",
                schema: "orchestration",
                table: "work_items",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_work_items_claim",
                schema: "orchestration",
                table: "work_items",
                columns: ["profile_key", "env_class", "created_at"],
                filter: "status = 'Queued'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_work_items_claim",
                schema: "orchestration",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "env_class",
                schema: "orchestration",
                table: "work_items");

            migrationBuilder.CreateIndex(
                name: "ix_work_items_claim",
                schema: "orchestration",
                table: "work_items",
                columns: ["profile_key", "created_at"],
                filter: "status = 'Queued'");
        }
    }
}
