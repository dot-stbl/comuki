using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Intake.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIntakeSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admission_rules",
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
                name: "intake_deliveries",
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
                    table.PrimaryKey("pk_intake_deliveries", static x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "intake_tickets",
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
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_intake_tickets", static x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "source_connections",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    settings_json = table.Column<string>(type: "jsonb", nullable: false),
                    secret_env_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
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
                        name: "fk_sync_jobs_intake_tickets_ticket_id",
                        column: static x => x.ticket_id,
                        principalTable: "intake_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admission_rules_project",
                table: "admission_rules",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_intake_deliveries_source_delivery",
                table: "intake_deliveries",
                columns: ["source", "delivery_id"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_intake_tickets_claimed",
                table: "intake_tickets",
                column: "updated_at",
                filter: "status = 'Claimed'");

            migrationBuilder.CreateIndex(
                name: "ix_intake_tickets_pending",
                table: "intake_tickets",
                column: "created_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_intake_tickets_active",
                table: "intake_tickets",
                columns: ["project_id", "provider", "external_id"],
                unique: true,
                filter: "status IN ('Pending', 'Claimed')");

            migrationBuilder.CreateIndex(
                name: "ix_source_connections_project",
                table: "source_connections",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_source_connections_webhook_key",
                table: "source_connections",
                column: "webhook_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_jobs_due",
                table: "sync_jobs",
                column: "next_attempt_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_sync_jobs_ticket_id",
                table: "sync_jobs",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ux_sync_jobs_run_id",
                table: "sync_jobs",
                column: "run_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admission_rules");

            migrationBuilder.DropTable(
                name: "intake_deliveries");

            migrationBuilder.DropTable(
                name: "source_connections");

            migrationBuilder.DropTable(
                name: "sync_jobs");

            migrationBuilder.DropTable(
                name: "intake_tickets");
        }
    }
}
