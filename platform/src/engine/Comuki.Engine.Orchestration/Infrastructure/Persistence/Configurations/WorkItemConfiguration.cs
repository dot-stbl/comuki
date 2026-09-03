using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;

/// <summary>
/// Work items table mapping: lease columns (uuid? / timestamptz?), brief as
/// jsonb, status as string, and the claim-path indexes — a partial index over
/// the live statuses plus a per-run lookup.
/// </summary>
public sealed class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.ToTable(OrchestrationDatabase.WorkItems, OrchestrationDatabase.Schema);
        builder.HasKey(static item => item.Id);

        builder.Property(static item => item.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(static item => item.RunId)
            .HasColumnName("run_id")
            .HasConversion(OrchestrationIdConverters.RunIdToUuid);

        builder.HasOne<Run>()
            .WithMany()
            .HasForeignKey(static item => item.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(static item => item.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static item => item.ProfileKey)
            .HasColumnName("profile_key")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static item => item.Image)
            .HasColumnName("image")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static item => item.ProfilesRef)
            .HasColumnName("profiles_ref")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(static item => item.Brief)
            .HasColumnName("brief")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(static item => item.LeasedBy)
            .HasColumnName("leased_by")
            .HasConversion(OrchestrationIdConverters.WorkerIdToUuid);

        builder.Property(static item => item.LeaseUntil)
            .HasColumnName("lease_until");

        builder.Property(static item => item.HeartbeatAt)
            .HasColumnName("heartbeat_at");

        builder.Property(static item => item.Attempt)
            .HasColumnName("attempt")
            .HasDefaultValue(0);

        builder.Property(static item => item.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static item => item.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(static item => item.RunId)
            .HasDatabaseName("ix_work_items_run_id");

        // claim scans only ever look at live statuses — keep the index small.
        // EF's HasConversion<string> stores PascalCase enum names, so the
        // predicate must match them or the index would never be used.
        builder.HasIndex(static item => new { item.Status, item.CreatedAt })
            .HasDatabaseName("ix_work_items_active")
            .HasFilter("status IN ('Queued', 'Running')");

        // the claim subselect: profile match + FIFO within one profile, live rows only
        builder.HasIndex(static item => new { item.ProfileKey, item.CreatedAt })
            .HasDatabaseName("ix_work_items_claim")
            .HasFilter("status = 'Queued'");
    }
}
