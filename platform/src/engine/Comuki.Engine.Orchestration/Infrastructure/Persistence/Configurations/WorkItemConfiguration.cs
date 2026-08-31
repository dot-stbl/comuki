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
        _ = builder.ToTable(OrchestrationTables.WorkItems);
        _ = builder.HasKey(static item => item.Id);

        _ = builder.Property(static item => item.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        _ = builder.Property(static item => item.RunId)
            .HasColumnName("run_id")
            .HasConversion(OrchestrationIdConverters.RunIdToUuid);

        _ = builder.HasOne<Run>()
            .WithMany()
            .HasForeignKey(static item => item.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Property(static item => item.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        _ = builder.Property(static item => item.ProfileKey)
            .HasColumnName("profile_key")
            .HasMaxLength(128)
            .IsRequired();

        _ = builder.Property(static item => item.Image)
            .HasColumnName("image")
            .HasMaxLength(512)
            .IsRequired();

        _ = builder.Property(static item => item.ProfilesRef)
            .HasColumnName("profiles_ref")
            .HasMaxLength(256)
            .IsRequired();

        _ = builder.Property(static item => item.Brief)
            .HasColumnName("brief")
            .HasColumnType("jsonb")
            .IsRequired();

        _ = builder.Property(static item => item.LeasedBy)
            .HasColumnName("leased_by")
            .HasConversion(OrchestrationIdConverters.WorkerIdToUuid);

        _ = builder.Property(static item => item.LeaseUntil)
            .HasColumnName("lease_until");

        _ = builder.Property(static item => item.HeartbeatAt)
            .HasColumnName("heartbeat_at");

        _ = builder.Property(static item => item.Attempt)
            .HasColumnName("attempt")
            .HasDefaultValue(0);

        _ = builder.Property(static item => item.CreatedAt)
            .HasColumnName("created_at");

        _ = builder.Property(static item => item.UpdatedAt)
            .HasColumnName("updated_at");

        _ = builder.HasIndex(static item => item.RunId)
            .HasDatabaseName("ix_work_items_run_id");

        // claim scans only ever look at live statuses — keep the index small.
        // EF's HasConversion<string> stores PascalCase enum names, so the
        // predicate must match them or the index would never be used.
        _ = builder.HasIndex(static item => new { item.Status, item.CreatedAt })
            .HasDatabaseName("ix_work_items_active")
            .HasFilter("status IN ('Queued', 'Running')");

        // the claim subselect: profile match + FIFO within one profile, live rows only
        _ = builder.HasIndex(static item => new { item.ProfileKey, item.CreatedAt })
            .HasDatabaseName("ix_work_items_claim")
            .HasFilter("status = 'Queued'");
    }
}
