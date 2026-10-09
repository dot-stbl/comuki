using Comuki.Modules.Work.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Work.Infrastructure.Persistence.Configurations;

/// <summary>WorkTaskAssignments mapping: responsible-actor metadata.</summary>
public sealed class WorkTaskAssignmentConfiguration : IEntityTypeConfiguration<WorkTaskAssignmentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkTaskAssignmentEntity> builder)
    {
        builder.ToTable(WorkDatabase.WorkTaskAssignments, WorkDatabase.Schema);
        builder.HasKey(static assignment => assignment.Id);

        builder.Property(static assignment => assignment.Id)
            .HasColumnName("id");

        builder.Property(static assignment => assignment.TaskId)
            .HasColumnName("task_id")
            .IsRequired();

        builder.Property(static assignment => assignment.ActorKind)
            .HasColumnName("actor_kind")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static assignment => assignment.ActorId)
            .HasColumnName("actor_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static assignment => assignment.CapacityHint)
            .HasColumnName("capacity_hint");

        builder.Property(static assignment => assignment.ProposalRequired)
            .HasColumnName("proposal_required")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(static assignment => assignment.ProposalState)
            .HasColumnName("proposal_state")
            .HasMaxLength(16);

        builder.Property(static assignment => assignment.ProposedBy)
            .HasColumnName("proposed_by")
            .HasMaxLength(128);

        builder.Property(static assignment => assignment.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        // Per-Task actor roster query (the dashboard's "responsible actors" panel).
        builder.HasIndex(static assignment => assignment.TaskId)
            .HasDatabaseName("ix_work_task_assignments_task_id");

        // No duplicate (task, actor) rows.
        builder.HasIndex(static assignment => new { assignment.TaskId, assignment.ActorKind, assignment.ActorId })
            .IsUnique()
            .HasDatabaseName("ux_work_task_assignments_actor");
    }
}
