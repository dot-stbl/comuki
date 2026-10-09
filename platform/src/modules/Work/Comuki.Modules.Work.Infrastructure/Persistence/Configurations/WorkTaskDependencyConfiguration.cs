using Comuki.Modules.Work.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Work.Infrastructure.Persistence.Configurations;

/// <summary>WorkTaskDependencies mapping: outgoing edges of the Task graph.</summary>
public sealed class WorkTaskDependencyConfiguration : IEntityTypeConfiguration<WorkTaskDependencyEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkTaskDependencyEntity> builder)
    {
        builder.ToTable(WorkDatabase.WorkTaskDependencies, WorkDatabase.Schema);
        builder.HasKey(static edge => edge.Id);

        builder.Property(static edge => edge.Id)
            .HasColumnName("id");

        builder.Property(static edge => edge.TaskId)
            .HasColumnName("task_id")
            .IsRequired();

        builder.Property(static edge => edge.PrerequisiteId)
            .HasColumnName("prerequisite_id")
            .IsRequired();

        builder.Property(static edge => edge.Kind)
            .HasColumnName("kind")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static edge => edge.CrossMission)
            .HasColumnName("cross_mission")
            .IsRequired()
            .HasDefaultValue(false);

        // No duplicate (task, prerequisite, kind) edges. The aggregate
        // rejects duplicates on its own; the unique index is a backstop.
        builder.HasIndex(static edge => new { edge.TaskId, edge.PrerequisiteId, edge.Kind })
            .IsUnique()
            .HasDatabaseName("ux_work_task_dependencies_prereq_kind");

        // "What does this Task block?" reverse lookup.
        builder.HasIndex(static edge => edge.PrerequisiteId)
            .HasDatabaseName("ix_work_task_dependencies_prerequisite_id");
    }
}
