using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Costs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "costs");

            migrationBuilder.RenameTable(
                name: "usage_events",
                newName: "usage_events",
                newSchema: "costs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "usage_events",
                schema: "costs",
                newName: "usage_events");
        }
    }
}
