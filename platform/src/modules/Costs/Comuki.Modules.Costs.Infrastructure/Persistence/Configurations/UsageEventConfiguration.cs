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
        builder.ToTable(CostsTables.UsageEvents);
        builder.HasKey(static usageEvent => usageEvent.Id);

        builder.Property(static usageEvent => usageEvent.Id)
            .HasColumnName("id")
            .HasConversion(CostsIdConverters.UsageEventIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static usageEvent => usageEvent.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(CostsIdConverters.ProjectIdToUuid)
            .IsRequired();

        builder.Property(static usageEvent => usageEvent.RunId)
            .HasColumnName("run_id")
            .HasConversion(CostsIdConverters.RunIdToUuid);

        builder.Property(static usageEvent => usageEvent.Source)
            .HasColumnName("source")
            .HasConversion(CostsKeyConverters.SourceToKey)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static usageEvent => usageEvent.Model)
            .HasColumnName("model")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static usageEvent => usageEvent.InputTokens)
            .HasColumnName("input_tokens");

        builder.Property(static usageEvent => usageEvent.OutputTokens)
            .HasColumnName("output_tokens");

        builder.Property(static usageEvent => usageEvent.CostUsdMicros)
            .HasColumnName("cost_usd_micros");

        builder.Property(static usageEvent => usageEvent.OccurredAt)
            .HasColumnName("occurred_at");

        builder.HasIndex(static usageEvent => new { usageEvent.ProjectId, usageEvent.OccurredAt })
            .HasDatabaseName("ix_usage_events_project_occurred");

        builder.HasIndex(static usageEvent => usageEvent.RunId)
            .HasDatabaseName("ix_usage_events_run_id");
    }
}
