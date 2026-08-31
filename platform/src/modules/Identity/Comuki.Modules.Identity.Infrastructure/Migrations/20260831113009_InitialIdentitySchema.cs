using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentitySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.CreateTable(
                name: "role_assignments",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scope_project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_by_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    granted_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: static table =>
                {
                    _ = table.PrimaryKey("pk_role_assignments", static x => x.id);
                });

            _ = migrationBuilder.CreateTable(
                name: "users",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    tokens_version = table.Column<int>(type: "integer", nullable: false),
                    disabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    _ = table.PrimaryKey("pk_users", static x => x.id);
                });

            _ = migrationBuilder.CreateTable(
                name: "api_keys",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    prefix = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    key_hmac = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: static table =>
                {
                    _ = table.PrimaryKey("pk_api_keys", static x => x.id);
                    _ = table.ForeignKey(
                        name: "fk_api_keys_users_user_id",
                        column: static x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            _ = migrationBuilder.CreateTable(
                name: "oidc_links",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sub = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    _ = table.PrimaryKey("pk_oidc_links", static x => x.id);
                    _ = table.ForeignKey(
                        name: "fk_oidc_links_users_user_id",
                        column: static x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            _ = migrationBuilder.CreateIndex(
                name: "ix_api_keys_prefix",
                table: "api_keys",
                column: "prefix",
                unique: true);

            _ = migrationBuilder.CreateIndex(
                name: "ix_api_keys_user_id",
                table: "api_keys",
                column: "user_id");

            _ = migrationBuilder.CreateIndex(
                name: "ix_oidc_links_provider_sub",
                table: "oidc_links",
                columns: ["provider", "sub"],
                unique: true);

            _ = migrationBuilder.CreateIndex(
                name: "ix_oidc_links_user_id",
                table: "oidc_links",
                column: "user_id");

            _ = migrationBuilder.CreateIndex(
                name: "ix_role_assignments_active_platform",
                table: "role_assignments",
                columns: ["subject_type", "subject_id", "role"],
                unique: true,
                filter: "revoked_at IS NULL AND scope_project_id IS NULL");

            _ = migrationBuilder.CreateIndex(
                name: "ix_role_assignments_active_project",
                table: "role_assignments",
                columns: ["subject_type", "subject_id", "role", "scope_project_id"],
                unique: true,
                filter: "revoked_at IS NULL AND scope_project_id IS NOT NULL");

            _ = migrationBuilder.CreateIndex(
                name: "ix_role_assignments_subject",
                table: "role_assignments",
                columns: ["subject_type", "subject_id"],
                filter: "revoked_at IS NULL");

            _ = migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.DropTable(
                name: "api_keys");

            _ = migrationBuilder.DropTable(
                name: "oidc_links");

            _ = migrationBuilder.DropTable(
                name: "role_assignments");

            _ = migrationBuilder.DropTable(
                name: "users");
        }
    }
}
