using Comuki.Modules.Work.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Work.Infrastructure.Persistence.Configurations;

/// <summary>WorkTasks mapping: scalar aggregate columns + the <c>bigint</c> Version token (per <c>add-work-management/design.md</c> §Persistence — manual optimistic concurrency).</summary>
public sealed class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTaskEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkTaskEntity> builder)
    {
        builder.ToTable(WorkDatabase.WorkTasks, WorkDatabase.Schema);
        builder.HasKey(static task => task.Id);

        builder.Property(static task => task.Id)
            .HasColumnName("id");

        builder.Property(static task => task.ProjectId)
            .HasColumnName("project_id");

        builder.Property(static task => task.Title)
            .HasColumnName("title")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static task => task.Brief)
            .HasColumnName("brief")
            .HasMaxLength(8192)
            .IsRequired();

        builder.Property(static task => task.BriefVersion)
            .HasColumnName("brief_version")
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(static task => task.AttemptOrdinal)
            .HasColumnName("attempt_ordinal")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(static task => task.ActiveAttemptId)
            .HasColumnName("active_attempt_id");

        builder.Property(static task => task.Visibility)
            .HasColumnName("visibility")
            .HasMaxLength(16)
            .IsRequired()
            .HasDefaultValue("Project");

        builder.Property(static task => task.MissionId)
            .HasColumnName("mission_id");

        builder.Property(static task => task.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .IsRequired()
            .HasDefaultValue("Draft");

        builder.Property(static task => task.ResolutionOutcome)
            .HasColumnName("resolution_outcome")
            .HasMaxLength(16);

        // bigint Version OptimisticConcurrency token (per architecture.md §Persistence).
        // Manually bumped on every save; EF compares OriginalValue against the
        // loaded row, throwing DbUpdateConcurrencyException on mismatch.
        builder.Property(static task => task.Version)
            .HasColumnName("version")
            .IsRequired()
            .IsConcurrencyToken()
            .HasDefaultValue(1L);

        builder.Property(static task => task.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(static task => task.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // Per-project list query (the dashboard's default WorkTasks list).
        builder.HasIndex(static task => new { task.ProjectId, task.Status })
            .HasDatabaseName("ix_work_tasks_project_id_status");

        // Per-project updated-at (the dispatcher's recent-activity path).
        builder.HasIndex(static task => new { task.ProjectId, task.UpdatedAt })
            .HasDatabaseName("ix_work_tasks_project_id_updated_at");

        // Cross-Mission terminal-state query (the WorkTaskRunView projection).
        builder.HasIndex(static task => new { task.Status, task.ResolutionOutcome })
            .HasDatabaseName("ix_work_tasks_status_outcome")
            .HasFilter("resolution_outcome IS NOT NULL");
    }
}
