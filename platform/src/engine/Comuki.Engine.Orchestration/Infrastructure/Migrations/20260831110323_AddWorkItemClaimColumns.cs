using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Engine.Orchestration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemClaimColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.DropIndex(
                name: "ix_work_items_active",
                table: "work_items");

            _ = migrationBuilder.AddColumn<int>(
                name: "attempt",
                table: "work_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            _ = migrationBuilder.AddColumn<string>(
                name: "image",
                table: "work_items",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            _ = migrationBuilder.AddColumn<string>(
                name: "profiles_ref",
                table: "work_items",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            _ = migrationBuilder.CreateIndex(
                name: "ix_work_items_active",
                table: "work_items",
                columns: ["status", "created_at"],
                filter: "status IN ('Queued', 'Running')");

            _ = migrationBuilder.CreateIndex(
                name: "ix_work_items_claim",
                table: "work_items",
                columns: ["profile_key", "created_at"],
                filter: "status = 'Queued'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.DropIndex(
                name: "ix_work_items_active",
                table: "work_items");

            _ = migrationBuilder.DropIndex(
                name: "ix_work_items_claim",
                table: "work_items");

            _ = migrationBuilder.DropColumn(
                name: "attempt",
                table: "work_items");

            _ = migrationBuilder.DropColumn(
                name: "image",
                table: "work_items");

            _ = migrationBuilder.DropColumn(
                name: "profiles_ref",
                table: "work_items");

            _ = migrationBuilder.CreateIndex(
                name: "ix_work_items_active",
                table: "work_items",
                columns: ["status", "created_at"],
                filter: "status IN ('queued', 'running')");
        }
    }
}
