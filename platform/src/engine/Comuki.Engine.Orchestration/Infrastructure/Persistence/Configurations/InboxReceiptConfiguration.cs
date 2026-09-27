using Comuki.Engine.Orchestration.Domain.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;

/// <summary>
/// Inbox-receipt table mapping: the wire message id is the primary key
/// (string, bounded), with a single <c>received_at</c> timestamp. The PK
/// uniqueness constraint is what makes <c>INSERT ... ON CONFLICT
/// (message_id) DO NOTHING</c> race-safe.
/// </summary>
public sealed class InboxReceiptConfiguration : IEntityTypeConfiguration<InboxReceipt>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InboxReceipt> builder)
    {
        builder.ToTable(OrchestrationDatabase.InboxReceipts, OrchestrationDatabase.Schema);
        builder.HasKey(static receipt => receipt.MessageId);

        builder.Property(static receipt => receipt.MessageId)
            .HasColumnName("message_id")
            .HasMaxLength(256)
            .ValueGeneratedNever();

        builder.Property(static receipt => receipt.ReceivedAt)
            .HasColumnName("received_at")
            .IsRequired();
    }
}
