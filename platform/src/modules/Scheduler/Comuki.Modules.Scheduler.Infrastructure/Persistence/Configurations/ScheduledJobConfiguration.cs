using Comuki.Modules.Scheduler.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Scheduler.Infrastructure.Persistence.Configurations;

/// <summary>
/// scheduled_jobs mapping: project id is a plain uuid value (no FK to the
/// projects schema — different context); brief_json is jsonb;
/// <c>next_fire_at</c> drives the dispatcher's due query and is
/// indexed.
/// </summary>
public sealed class ScheduledJobConfiguration : IEntityTypeConfiguration<ScheduledJob>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ScheduledJob> builder)
    {
        builder.ToTable(SchedulerDatabase.ScheduledJobs, SchedulerDatabase.Schema);
        builder.HasKey(static job => job.Id);

        builder.Property(static job => job.Id)
            .HasColumnName("id")
            .HasConversion(SchedulerIdConverters.ScheduledJobIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static job => job.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(SchedulerIdConverters.ProjectIdToUuid);

        builder.Property(static job => job.CronExpression)
            .HasColumnName("cron_expression")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(static job => job.ProfileKey)
            .HasColumnName("profile_key")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(static job => job.BriefJson)
            .HasColumnName("brief_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(static job => job.RunOnOnceAt)
            .HasColumnName("run_on_once_at");

        builder.Property(static job => job.Enabled)
            .HasColumnName("enabled")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(static job => job.LastFiredAt)
            .HasColumnName("last_fired_at");

        builder.Property(static job => job.NextFireAt)
            .HasColumnName("next_fire_at")
            .IsRequired();

        builder.Property(static job => job.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(static job => job.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // Due-job query: WHERE enabled = TRUE AND next_fire_at <= now.
        builder.HasIndex(static job => new { job.Enabled, job.NextFireAt })
            .HasDatabaseName("ix_scheduled_jobs_due")
            .HasFilter("enabled = TRUE");

        // Per-project listing.
        builder.HasIndex(static job => job.ProjectId)
            .HasDatabaseName("ix_scheduled_jobs_project");
    }
}
