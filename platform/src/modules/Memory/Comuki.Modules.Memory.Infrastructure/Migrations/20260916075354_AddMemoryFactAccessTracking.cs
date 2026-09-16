using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Memory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMemoryFactAccessTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_read_at",
                schema: "memory",
                table: "memory_facts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "read_count",
                schema: "memory",
                table: "memory_facts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_read_at",
                schema: "memory",
                table: "memory_facts");

            migrationBuilder.DropColumn(
                name: "read_count",
                schema: "memory",
                table: "memory_facts");
        }
    }
}
