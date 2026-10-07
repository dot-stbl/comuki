using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIntegrationsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integrations");

            migrationBuilder.CreateTable(
                name: "admission_rules",
                schema: "integrations",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    filter_json = table.Column<string>(type: "jsonb", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_admission_rules", static x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "integrations",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    delivery_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    detail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_deliveries", static x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbound_items",
                schema: "integrations",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    body = table.Column<string>(type: "character varying(32768)", maxLength: 32768, nullable: false),
                    author = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    project_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    labels = table.Column<string[]>(type: "text[]", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_inbound_items", static x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "source_connections",
                schema: "integrations",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    settings_json = table.Column<string>(type: "jsonb", nullable: false),
                    secret_env_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    webhook_secret = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    webhook_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_source_connections", static x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_jobs",
                schema: "integrations",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    external_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    run_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_sync_jobs", static x => x.id);
                    table.ForeignKey(
                        name: "fk_sync_jobs_inbound_items_ticket_id",
                        column: static x => x.ticket_id,
                        principalSchema: "integrations",
                        principalTable: "inbound_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admission_rules_project",
                schema: "integrations",
                table: "admission_rules",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_deliveries_source_delivery",
                schema: "integrations",
                table: "deliveries",
                columns: ["source", "delivery_id"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_items_claimed",
                schema: "integrations",
                table: "inbound_items",
                column: "updated_at",
                filter: "status = 'Claimed'");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_items_pending",
                schema: "integrations",
                table: "inbound_items",
                column: "created_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_inbound_items_active",
                schema: "integrations",
                table: "inbound_items",
                columns: ["project_id", "provider", "external_id"],
                unique: true,
                filter: "status IN ('Pending', 'Claimed')");

            migrationBuilder.CreateIndex(
                name: "ix_source_connections_project",
                schema: "integrations",
                table: "source_connections",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_source_connections_webhook_key",
                schema: "integrations",
                table: "source_connections",
                column: "webhook_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_jobs_due",
                schema: "integrations",
                table: "sync_jobs",
                column: "next_attempt_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_sync_jobs_ticket_id",
                schema: "integrations",
                table: "sync_jobs",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ux_sync_jobs_run_id",
                schema: "integrations",
                table: "sync_jobs",
                column: "run_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admission_rules",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "source_connections",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "sync_jobs",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "inbound_items",
                schema: "integrations");
        }
    }
}
