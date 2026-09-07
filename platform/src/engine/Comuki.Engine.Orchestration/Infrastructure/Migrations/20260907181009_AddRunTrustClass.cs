using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Engine.Orchestration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRunTrustClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "trust_class",
                schema: "orchestration",
                table: "runs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Supervised");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "trust_class",
                schema: "orchestration",
                table: "runs");
        }
    }
}
