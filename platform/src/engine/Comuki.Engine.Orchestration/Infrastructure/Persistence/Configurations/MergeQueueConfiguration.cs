using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;

/// <summary>
/// Merge-queue table mapping: uuid id, project_id nullable (cross-project
/// merges allowed), branch + PR URL as bounded strings, status and
/// conflict_resolution as short string enums, claim / merge / abandon
/// timestamps with the operator id who drove the merge. Snake_case naming
/// is applied by <c>UseSnakeCaseNamingConvention</c> at the context
/// level; explicit column names keep the migration snapshot stable.
/// </summary>
public sealed class MergeQueueConfiguration : IEntityTypeConfiguration<MergeQueueEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MergeQueueEntry> builder)
    {
        _ = builder.ToTable(OrchestrationDatabase.MergeQueue, OrchestrationDatabase.Schema);
        _ = builder.HasKey(static entry => entry.Id);

        _ = builder.Property(static entry => entry.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        _ = builder.Property(static entry => entry.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(OrchestrationIdConverters.ProjectIdToUuid);

        _ = builder.Property(static entry => entry.BranchName)
            .HasColumnName("branch_name")
            .HasMaxLength(256)
            .IsRequired();

        _ = builder.Property(static entry => entry.PullRequestUrl)
            .HasColumnName("pull_request_url")
            .HasMaxLength(1024)
            .IsRequired();

        _ = builder.Property(static entry => entry.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        _ = builder.Property(static entry => entry.ConflictResolution)
            .HasColumnName("conflict_resolution")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        _ = builder.Property(static entry => entry.EnqueuedAt)
            .HasColumnName("enqueued_at");

        _ = builder.Property(static entry => entry.ClaimedBy)
            .HasColumnName("claimed_by")
            .HasMaxLength(128);

        _ = builder.Property(static entry => entry.ClaimedAt)
            .HasColumnName("claimed_at");

        _ = builder.Property(static entry => entry.MergedAt)
            .HasColumnName("merged_at");

        _ = builder.Property(static entry => entry.AbandonedAt)
            .HasColumnName("abandoned_at");

        _ = builder.Property(static entry => entry.AbandonedReason)
            .HasColumnName("abandoned_reason")
            .HasMaxLength(1024);

        _ = builder.Property(static entry => entry.Notes)
            .HasColumnName("notes")
            .HasMaxLength(4096);

        // Operator picks the next pending entry across the whole project
        // (or, with a project filter, just that project). FIFO by enqueue
        // time inside the live status set keeps the dashboard predictable.
        _ = builder.HasIndex(static entry => new { entry.Status, entry.EnqueuedAt })
            .HasDatabaseName("ix_merge_queue_status_enqueued_at");

        _ = builder.HasIndex(static entry => entry.ProjectId)
            .HasDatabaseName("ix_merge_queue_project_id");
    }
}
