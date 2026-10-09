using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Work.Infrastructure.Migrations
{
    /// <summary>
    /// Work module schema bootstrap (<c>add-work-management</c> task 3.2):
    /// the six aggregate tables (work_tasks + five side tables),
    /// the Work-side mirror <c>inbox_receipts</c> dedupe ledger
    /// (per <c>design.md</c> Open Question A's mirror-table
    /// recommendation), the <c>inbound_item_bindings</c> map the
    /// Integrations-admission hook reads to find the original Task
    /// on replay, and the per-subscriber outbox-poll watermark
    /// table. <c>work_tasks.version</c> is the bigint optimistic-
    /// concurrency token (per architecture.md §Persistence) —
    /// bumped on every save.
    /// </summary>
    public partial class WorkInit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "work");

            migrationBuilder.CreateTable(
                name: "work_tasks",
                schema: "work",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    brief = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    brief_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    attempt_ordinal = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    active_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Project"),
                    mission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Draft"),
                    resolution_outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_work_tasks", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_tasks_project_id_status",
                schema: "work",
                table: "work_tasks",
                columns: ["project_id", "status"]);

            migrationBuilder.CreateIndex(
                name: "ix_work_tasks_project_id_updated_at",
                schema: "work",
                table: "work_tasks",
                columns: ["project_id", "updated_at"]);

            migrationBuilder.CreateIndex(
                name: "ix_work_tasks_status_outcome",
                schema: "work",
                table: "work_tasks",
                columns: ["status", "resolution_outcome"],
                filter: "resolution_outcome IS NOT NULL");

            migrationBuilder.CreateTable(
                name: "work_task_attempts",
                schema: "work",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_ordinal = table.Column<int>(type: "integer", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    terminal_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terminal_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_work_task_attempts", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_work_task_attempts_ordinal",
                schema: "work",
                table: "work_task_attempts",
                columns: ["task_id", "attempt_ordinal"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_task_attempts_run_id",
                schema: "work",
                table: "work_task_attempts",
                column: "run_id");

            migrationBuilder.CreateTable(
                name: "work_task_source_refs",
                schema: "work",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    display_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    link_note = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_work_task_source_refs", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_task_source_refs_task_id",
                schema: "work",
                table: "work_task_source_refs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ux_work_task_source_refs_primary",
                schema: "work",
                table: "work_task_source_refs",
                column: "task_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateTable(
                name: "work_task_dependencies",
                schema: "work",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prerequisite_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cross_mission = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_work_task_dependencies", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_work_task_dependencies_prereq_kind",
                schema: "work",
                table: "work_task_dependencies",
                columns: ["task_id", "prerequisite_id", "kind"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_task_dependencies_prerequisite_id",
                schema: "work",
                table: "work_task_dependencies",
                column: "prerequisite_id");

            migrationBuilder.CreateTable(
                name: "work_task_assignments",
                schema: "work",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    capacity_hint = table.Column<int>(type: "integer", nullable: true),
                    proposal_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    proposal_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    proposed_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_work_task_assignments", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_task_assignments_task_id",
                schema: "work",
                table: "work_task_assignments",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ux_work_task_assignments_actor",
                schema: "work",
                table: "work_task_assignments",
                columns: ["task_id", "actor_kind", "actor_id"],
                unique: true);

            migrationBuilder.CreateTable(
                name: "work_task_completion_policies",
                schema: "work",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    policy_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    evidence_contract = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_work_task_completion_policies", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_work_task_completion_policies_task_version",
                schema: "work",
                table: "work_task_completion_policies",
                columns: ["task_id", "version"],
                unique: true);

            migrationBuilder.CreateTable(
                name: "inbox_receipts",
                schema: "work",
                columns: static table => new
                {
                    message_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_inbox_receipts", static x => x.message_id);
                });

            migrationBuilder.CreateTable(
                name: "inbound_item_bindings",
                schema: "work",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inbound_item_external_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_inbound_item_bindings", static x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_inbound_item_bindings_inbound",
                schema: "work",
                table: "inbound_item_bindings",
                column: "inbound_item_external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_item_bindings_task_id",
                schema: "work",
                table: "inbound_item_bindings",
                column: "task_id");

            migrationBuilder.CreateTable(
                name: "outbox_watermarks",
                schema: "work",
                columns: static table => new
                {
                    subscriber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    last_seen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: static table =>
                {
                    table.PrimaryKey("pk_outbox_watermarks", static x => new { x.subscriber, x.type });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "outbox_watermarks", schema: "work");
            migrationBuilder.DropTable(name: "inbound_item_bindings", schema: "work");
            migrationBuilder.DropTable(name: "inbox_receipts", schema: "work");
            migrationBuilder.DropTable(name: "work_task_completion_policies", schema: "work");
            migrationBuilder.DropTable(name: "work_task_assignments", schema: "work");
            migrationBuilder.DropTable(name: "work_task_dependencies", schema: "work");
            migrationBuilder.DropTable(name: "work_task_source_refs", schema: "work");
            migrationBuilder.DropTable(name: "work_task_attempts", schema: "work");
            migrationBuilder.DropTable(name: "work_tasks", schema: "work");
        }
    }
}
