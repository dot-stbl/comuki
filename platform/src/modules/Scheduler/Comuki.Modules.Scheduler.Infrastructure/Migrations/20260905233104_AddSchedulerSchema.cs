using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comuki.Modules.Scheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduler");

            migrationBuilder.CreateTable(
                name: "scheduled_jobs",
                schema: "scheduler",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cron_expression = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    profile_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    brief_json = table.Column<string>(type: "jsonb", nullable: false),
                    run_on_once_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_fire_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_jobs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_jobs_due",
                schema: "scheduler",
                table: "scheduled_jobs",
                columns: new[] { "enabled", "next_fire_at" },
                filter: "enabled = TRUE");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_jobs_project",
                schema: "scheduler",
                table: "scheduled_jobs",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_jobs",
                schema: "scheduler");
        }
    }
}
