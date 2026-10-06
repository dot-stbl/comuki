using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Engine.Orchestration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationRecordAndMergeQueueRunReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "run_id",
                schema: "orchestration",
                table: "merge_queue",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "run_id",
                schema: "orchestration",
                table: "merge_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "verifications",
                schema: "orchestration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gate_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    verdict = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    evidence_refs = table.Column<string>(type: "jsonb", nullable: false),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    evaluator = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_merge_queue_run_id",
                schema: "orchestration",
                table: "merge_queue",
                column: "run_id",
                filter: "run_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_verifications_work_item_id",
                schema: "orchestration",
                table: "verifications",
                column: "work_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_verifications_work_item_id_gate_name",
                schema: "orchestration",
                table: "verifications",
                columns: new[] { "work_item_id", "gate_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "verifications",
                schema: "orchestration");

            migrationBuilder.DropIndex(
                name: "ix_merge_queue_run_id",
                schema: "orchestration",
                table: "merge_queue");

            migrationBuilder.DropColumn(
                name: "run_id",
                schema: "orchestration",
                table: "merge_queue");

            migrationBuilder.DropColumn(
                name: "run_id",
                schema: "orchestration",
                table: "merge_batches");
        }
    }
}
