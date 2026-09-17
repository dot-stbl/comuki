using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Memory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningCandidateSuggestionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "pattern",
                schema: "memory",
                table: "learning_candidates",
                newName: "proposed_rule");

            migrationBuilder.AddColumn<string>(
                name: "decision_reason",
                schema: "memory",
                table: "learning_candidates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "observation",
                schema: "memory",
                table: "learning_candidates",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                schema: "memory",
                table: "learning_candidates",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "topic",
                schema: "memory",
                table: "learning_candidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "decision_reason",
                schema: "memory",
                table: "learning_candidates");

            migrationBuilder.DropColumn(
                name: "observation",
                schema: "memory",
                table: "learning_candidates");

            migrationBuilder.DropColumn(
                name: "project_id",
                schema: "memory",
                table: "learning_candidates");

            migrationBuilder.DropColumn(
                name: "topic",
                schema: "memory",
                table: "learning_candidates");

            migrationBuilder.RenameColumn(
                name: "proposed_rule",
                schema: "memory",
                table: "learning_candidates",
                newName: "pattern");
        }
    }
}
