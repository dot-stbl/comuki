using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectBudgetCaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "hard_budget_usd_micros",
                table: "project_settings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "soft_budget_usd_micros",
                table: "project_settings",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hard_budget_usd_micros",
                table: "project_settings");

            migrationBuilder.DropColumn(
                name: "soft_budget_usd_micros",
                table: "project_settings");
        }
    }
}
