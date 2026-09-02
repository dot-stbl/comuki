using Comuki.Modules.Costs.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Costs.Infrastructure.Persistence.Configurations;

/// <summary>
/// usage_events mapping: project + optional run attribution, model, tokens,
/// cost in USD micros, and indexes for project/run aggregation.
/// </summary>
public sealed class UsageEventConfiguration : IEntityTypeConfiguration<UsageEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UsageEvent> builder)
    {
        _ = builder.ToTable(CostsTables.UsageEvents);
        _ = builder.HasKey(static usageEvent => usageEvent.Id);

        _ = builder.Property(static usageEvent => usageEvent.Id)
            .HasColumnName("id")
            .HasConversion(CostsIdConverters.UsageEventIdToUuid)
            .ValueGeneratedNever();

        _ = builder.Property(static usageEvent => usageEvent.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(CostsIdConverters.ProjectIdToUuid)
            .IsRequired();

        _ = builder.Property(static usageEvent => usageEvent.RunId)
            .HasColumnName("run_id")
            .HasConversion(CostsIdConverters.RunIdToUuid);

        _ = builder.Property(static usageEvent => usageEvent.Source)
            .HasColumnName("source")
            .HasConversion(CostsKeyConverters.SourceToKey)
            .HasMaxLength(16)
            .IsRequired();

        _ = builder.Property(static usageEvent => usageEvent.Model)
            .HasColumnName("model")
            .HasMaxLength(128)
            .IsRequired();

        _ = builder.Property(static usageEvent => usageEvent.InputTokens)
            .HasColumnName("input_tokens");

        _ = builder.Property(static usageEvent => usageEvent.OutputTokens)
            .HasColumnName("output_tokens");

        _ = builder.Property(static usageEvent => usageEvent.CostUsdMicros)
            .HasColumnName("cost_usd_micros");

        _ = builder.Property(static usageEvent => usageEvent.OccurredAt)
            .HasColumnName("occurred_at");

        _ = builder.HasIndex(static usageEvent => new { usageEvent.ProjectId, usageEvent.OccurredAt })
            .HasDatabaseName("ix_usage_events_project_occurred");

        _ = builder.HasIndex(static usageEvent => usageEvent.RunId)
            .HasDatabaseName("ix_usage_events_run_id");
    }
}
