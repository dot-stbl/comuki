using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Engine.Orchestration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRunAdmissionMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "admission_message_id",
                schema: "orchestration",
                table: "runs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_runs_admission_message_id",
                schema: "orchestration",
                table: "runs",
                column: "admission_message_id",
                unique: true,
                filter: "admission_message_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_runs_admission_message_id",
                schema: "orchestration",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "admission_message_id",
                schema: "orchestration",
                table: "runs");
        }
    }
}
