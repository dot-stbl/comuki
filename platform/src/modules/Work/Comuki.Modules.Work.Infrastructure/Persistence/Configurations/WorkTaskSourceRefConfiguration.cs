using Comuki.Modules.Work.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Work.Infrastructure.Persistence.Configurations;

/// <summary>WorkTaskSourceRefs mapping: source-link rows; exactly one primary per task.</summary>
public sealed class WorkTaskSourceRefConfiguration : IEntityTypeConfiguration<WorkTaskSourceRefEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkTaskSourceRefEntity> builder)
    {
        builder.ToTable(WorkDatabase.WorkTaskSourceRefs, WorkDatabase.Schema);
        builder.HasKey(static source => source.Id);

        builder.Property(static source => source.Id)
            .HasColumnName("id");

        builder.Property(static source => source.TaskId)
            .HasColumnName("task_id")
            .IsRequired();

        builder.Property(static source => source.Kind)
            .HasColumnName("kind")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(static source => source.ExternalId)
            .HasColumnName("external_id")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static source => source.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static source => source.IsPrimary)
            .HasColumnName("is_primary")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(static source => source.LinkNote)
            .HasColumnName("link_note")
            .HasMaxLength(2048);

        // Per-task source lookup (the dashboard's "this task's sources" view).
        builder.HasIndex(static source => source.TaskId)
            .HasDatabaseName("ix_work_task_source_refs_task_id");

        // Defence in depth — the aggregate side rejects a second primary;
        // this index makes it impossible at the database layer too.
        builder.HasIndex(static source => source.TaskId)
            .HasDatabaseName("ux_work_task_source_refs_primary")
            .IsUnique()
            .HasFilter("is_primary");
    }
}
