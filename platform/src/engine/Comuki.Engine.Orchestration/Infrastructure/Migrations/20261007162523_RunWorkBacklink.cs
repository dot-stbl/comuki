using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Engine.Orchestration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RunWorkBacklink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_ordinal",
                schema: "orchestration",
                table: "runs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "predecessor_run_id",
                schema: "orchestration",
                table: "runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "task_id",
                schema: "orchestration",
                table: "runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "triggering_actor_id",
                schema: "orchestration",
                table: "runs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_runs_task_id",
                schema: "orchestration",
                table: "runs",
                column: "task_id",
                filter: "task_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_runs_task_id_attempt_ordinal",
                schema: "orchestration",
                table: "runs",
                columns: ["task_id", "attempt_ordinal"],
                unique: true,
                filter: "task_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_runs_task_id",
                schema: "orchestration",
                table: "runs");

            migrationBuilder.DropIndex(
                name: "ux_runs_task_id_attempt_ordinal",
                schema: "orchestration",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "attempt_ordinal",
                schema: "orchestration",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "predecessor_run_id",
                schema: "orchestration",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "task_id",
                schema: "orchestration",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "triggering_actor_id",
                schema: "orchestration",
                table: "runs");
        }
    }
}
