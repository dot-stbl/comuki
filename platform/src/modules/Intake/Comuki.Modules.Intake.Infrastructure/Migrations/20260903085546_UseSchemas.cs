using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Intake.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "intake");

            migrationBuilder.RenameTable(
                name: "sync_jobs",
                newName: "sync_jobs",
                newSchema: "intake");

            migrationBuilder.RenameTable(
                name: "source_connections",
                newName: "source_connections",
                newSchema: "intake");

            migrationBuilder.RenameTable(
                name: "intake_tickets",
                newName: "intake_tickets",
                newSchema: "intake");

            migrationBuilder.RenameTable(
                name: "intake_deliveries",
                newName: "intake_deliveries",
                newSchema: "intake");

            migrationBuilder.RenameTable(
                name: "admission_rules",
                newName: "admission_rules",
                newSchema: "intake");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "sync_jobs",
                schema: "intake",
                newName: "sync_jobs");

            migrationBuilder.RenameTable(
                name: "source_connections",
                schema: "intake",
                newName: "source_connections");

            migrationBuilder.RenameTable(
                name: "intake_tickets",
                schema: "intake",
                newName: "intake_tickets");

            migrationBuilder.RenameTable(
                name: "intake_deliveries",
                schema: "intake",
                newName: "intake_deliveries");

            migrationBuilder.RenameTable(
                name: "admission_rules",
                schema: "intake",
                newName: "admission_rules");
        }
    }
}
