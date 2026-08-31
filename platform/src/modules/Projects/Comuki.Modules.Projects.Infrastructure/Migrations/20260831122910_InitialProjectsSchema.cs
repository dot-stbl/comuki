using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialProjectsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projects",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    profiles_git_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    profiles_git_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_projects", static x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_settings",
                columns: static table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_idle = table.Column<int>(type: "integer", nullable: false),
                    max_concurrent = table.Column<int>(type: "integer", nullable: false),
                    idle_ttl_seconds = table.Column<int>(type: "integer", nullable: true),
                    approve_required = table.Column<bool>(type: "boolean", nullable: false),
                    knowledge_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    verify_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    proxy_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_project_settings", static x => x.project_id);
                    table.ForeignKey(
                        name: "fk_project_settings_projects_project_id",
                        column: static x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_projects_slug",
                table: "projects",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_settings");

            migrationBuilder.DropTable(
                name: "projects");
        }
    }
}
