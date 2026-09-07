using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainTypeAdmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "domain_type_admissions",
                schema: "projects",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    allowed_sources = table.Column<string[]>(type: "text[]", nullable: false),
                    denied_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_domain_type_admissions", static x => x.id);
                    table.ForeignKey(
                        name: "fk_domain_type_admissions_projects_project_id",
                        column: static x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_domain_type_admissions_project_domain",
                schema: "projects",
                table: "domain_type_admissions",
                columns: ["project_id", "domain_type"],
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "domain_type_admissions",
                schema: "projects");
        }
    }
}
