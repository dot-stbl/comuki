using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Procedures.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProceduresSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "procedures");

            migrationBuilder.CreateTable(
                name: "compiled_procedure_versions",
                schema: "procedures",
                columns: static table => new
                {
                    version_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    catalog_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_ref = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    graph_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_compiled_procedure_versions", static x => x.version_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_compiled_procedure_versions_project_procedure",
                schema: "procedures",
                table: "compiled_procedure_versions",
                columns: ["project_id", "procedure_key"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compiled_procedure_versions",
                schema: "procedures");
        }
    }
}
