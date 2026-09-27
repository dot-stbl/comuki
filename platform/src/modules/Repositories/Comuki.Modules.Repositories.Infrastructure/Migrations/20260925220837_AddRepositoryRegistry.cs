using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Repositories.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "repositories");

            migrationBuilder.CreateTable(
                name: "repositories",
                schema: "repositories",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    host = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    default_branch = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_repositories", static x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "repository_credential_refs",
                schema: "repositories",
                columns: static table => new
                {
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_ref = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    default_access = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_repository_credential_refs", static x => x.repository_id);
                    table.ForeignKey(
                        name: "fk_repository_credential_refs_repositories_repository_id",
                        column: static x => x.repository_id,
                        principalSchema: "repositories",
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repository_policies",
                schema: "repositories",
                columns: static table => new
                {
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_branches = table.Column<string[]>(type: "text[]", nullable: false),
                    required_checks = table.Column<string[]>(type: "text[]", nullable: false),
                    approvers = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_repository_policies", static x => x.repository_id);
                    table.ForeignKey(
                        name: "fk_repository_policies_repositories_repository_id",
                        column: static x => x.repository_id,
                        principalSchema: "repositories",
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_repositories_host_url",
                schema: "repositories",
                table: "repositories",
                columns: ["host", "url"],
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repository_credential_refs",
                schema: "repositories");

            migrationBuilder.DropTable(
                name: "repository_policies",
                schema: "repositories");

            migrationBuilder.DropTable(
                name: "repositories",
                schema: "repositories");
        }
    }
}
