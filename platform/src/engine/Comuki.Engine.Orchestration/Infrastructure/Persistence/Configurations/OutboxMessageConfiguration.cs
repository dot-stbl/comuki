using Comuki.Engine.Orchestration.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;

/// <summary>
/// Outbox-message table mapping: uuid id, contract name (bounded text),
/// raw JSON payload (jsonb), and the four lifecycle timestamps (dispatched
/// and dead-lettered are nullable). The partial index keeps the dispatch
/// sweep's hot read small — only undispatched rows live there.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <summary>
    /// Partial-index predicate over undispatched rows. Dispatched and
    /// dead-lettered rows are excluded so the sweep's read is bounded by
    /// the live backlog.
    /// </summary>
    internal const string UndispatchedFilter = "dispatched_at IS NULL";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable(OrchestrationDatabase.OutboxMessages, OrchestrationDatabase.Schema);
        builder.HasKey(static message => message.Id);

        builder.Property(static message => message.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(static message => message.Type)
            .HasColumnName("type")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(static message => message.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(static message => message.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static message => message.DispatchedAt)
            .HasColumnName("dispatched_at");

        builder.Property(static message => message.Attempts)
            .HasColumnName("attempts")
            .HasDefaultValue(0);

        builder.Property(static message => message.LastError)
            .HasColumnName("last_error");

        builder.Property(static message => message.DeadLetteredAt)
            .HasColumnName("dead_lettered_at");

        // Sweep scans ORDER BY created_at — index that. Filter is partial
        // over undispatched rows; once dispatched, the row is dead weight
        // for this index.
        builder.HasIndex(static message => new { message.DispatchedAt, message.CreatedAt })
            .HasDatabaseName("ix_outbox_messages_undispatched")
            .HasFilter(UndispatchedFilter);
    }
}
