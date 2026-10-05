using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Memory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningCandidateOutcomeCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "build_green_count",
                schema: "memory",
                table: "learning_candidates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "build_red_count",
                schema: "memory",
                table: "learning_candidates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "task_failed_count",
                schema: "memory",
                table: "learning_candidates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "task_succeeded_count",
                schema: "memory",
                table: "learning_candidates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "verify_fail_count",
                schema: "memory",
                table: "learning_candidates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "verify_pass_count",
                schema: "memory",
                table: "learning_candidates",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "build_green_count",
                schema: "memory",
                table: "learning_candidates");

            migrationBuilder.DropColumn(
                name: "build_red_count",
                schema: "memory",
                table: "learning_candidates");

            migrationBuilder.DropColumn(
                name: "task_failed_count",
                schema: "memory",
                table: "learning_candidates");

            migrationBuilder.DropColumn(
                name: "task_succeeded_count",
                schema: "memory",
                table: "learning_candidates");

            migrationBuilder.DropColumn(
                name: "verify_fail_count",
                schema: "memory",
                table: "learning_candidates");

            migrationBuilder.DropColumn(
                name: "verify_pass_count",
                schema: "memory",
                table: "learning_candidates");
        }
    }
}
