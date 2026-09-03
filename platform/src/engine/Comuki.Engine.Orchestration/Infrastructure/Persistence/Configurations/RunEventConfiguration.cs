using Comuki.Engine.Orchestration.Domain.Journal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;

/// <summary>Run journal mapping: payload jsonb, timeline index (run_id, occurred_at).</summary>
public sealed class RunEventConfiguration : IEntityTypeConfiguration<RunEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RunEvent> builder)
    {
        builder.ToTable(OrchestrationDatabase.RunEvents, OrchestrationDatabase.Schema);
        builder.HasKey(static runEvent => runEvent.Id);

        builder.Property(static runEvent => runEvent.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(static runEvent => runEvent.RunId)
            .HasColumnName("run_id")
            .HasConversion(OrchestrationIdConverters.RunIdToUuid);

        builder.HasOne<Domain.Runs.Run>()
            .WithMany()
            .HasForeignKey(static runEvent => runEvent.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(static runEvent => runEvent.Type)
            .HasColumnName("type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(static runEvent => runEvent.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(static runEvent => runEvent.OccurredAt)
            .HasColumnName("occurred_at");

        builder.HasIndex(static runEvent => new { runEvent.RunId, runEvent.OccurredAt })
            .HasDatabaseName("ix_run_events_run_id_occurred_at");
    }
}
