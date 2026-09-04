using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOidcState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "oidc_states",
                schema: "identity",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    code_verifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    code_challenge_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    redirect_uri = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    return_to = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_oidc_states", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_oidc_states_expires_at",
                schema: "identity",
                table: "oidc_states",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oidc_states",
                schema: "identity");
        }
    }
}
