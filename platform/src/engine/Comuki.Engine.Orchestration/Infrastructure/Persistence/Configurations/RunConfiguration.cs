using Comuki.Engine.Orchestration.Domain.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;

/// <summary>Runs table mapping: uuid ids, status as string, timestamptz.</summary>
public sealed class RunConfiguration : IEntityTypeConfiguration<Run>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Run> builder)
    {
        builder.ToTable(OrchestrationDatabase.Runs, OrchestrationDatabase.Schema);
        builder.HasKey(static run => run.Id);

        builder.Property(static run => run.Id)
            .HasColumnName("id")
            .HasConversion(OrchestrationIdConverters.RunIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static run => run.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(OrchestrationIdConverters.ProjectIdToUuid);

        builder.Property(static run => run.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static run => run.TrustClass)
            .HasColumnName("trust_class")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired()
            .HasDefaultValue(RunTrustClass.Supervised);

        builder.Property(static run => run.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static run => run.UpdatedAt)
            .HasColumnName("updated_at");

        // Performance audit (2026-09-09) §1.1: every escalation sweep,
        // runs-list page, and project-scope filter was a heap scan.
        // Status+UpdatedAt is the dominant read pattern
        // (`WHERE status = $1 AND updated_at < $2` for the sweeper, the
        // same shape with DESC ordering for the runs-list page);
        // ProjectId+Status covers the subject-scope filter
        // (`WHERE project_id = ANY($1) AND status = $2`).
        builder.HasIndex(static run => new { run.Status, run.UpdatedAt })
            .HasDatabaseName("ix_runs_status_updated_at");

        builder.HasIndex(static run => new { run.ProjectId, run.Status })
            .HasDatabaseName("ix_runs_project_id_status");

        builder.HasIndex(static run => run.UpdatedAt)
            .HasDatabaseName("ix_runs_updated_at");
    }
}
