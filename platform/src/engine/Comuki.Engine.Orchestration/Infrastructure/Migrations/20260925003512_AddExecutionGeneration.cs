using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Engine.Orchestration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "generation",
                schema: "orchestration",
                table: "work_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "generation",
                schema: "orchestration",
                table: "runs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Pre-existing work_items rows default to generation 0 (the
            // AddColumn default above) while their owning runs default to
            // 1 — a Running item claimed before this migration would never
            // again match its run's generation on heartbeat/complete/fail
            // (WorkItemQueueSql's guarded SQL: "AND generation = @generation"),
            // 409ing forever. Backfill every existing item to its run's
            // current generation in the same migration so deploy-time state
            // starts consistent.
            migrationBuilder.Sql(
                "UPDATE orchestration.work_items w "
                + "SET generation = r.generation "
                + "FROM orchestration.runs r "
                + "WHERE w.run_id = r.id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "generation",
                schema: "orchestration",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "generation",
                schema: "orchestration",
                table: "runs");
        }
    }
}
