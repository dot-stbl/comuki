using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Verify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGenericCommandRunsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "verify");

            migrationBuilder.CreateTable(
                name: "generic_command_runs",
                schema: "verify",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    profile_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    executable = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    arguments = table.Column<string>(type: "jsonb", nullable: false),
                    expected_exit_code = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actual_exit_code = table.Column<int>(type: "integer", nullable: true),
                    output_log = table.Column<string>(type: "text", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_generic_command_runs", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_generic_command_runs_project",
                schema: "verify",
                table: "generic_command_runs",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_generic_command_runs_status",
                schema: "verify",
                table: "generic_command_runs",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "generic_command_runs",
                schema: "verify");
        }
    }
}
