using Comuki.Modules.Work.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Work.Infrastructure.Persistence.Configurations;

/// <summary>InboxReceipts mapping: dedupe PK on <c>message_id</c> per the WS9 admission-claim pattern.</summary>
public sealed class InboxReceiptConfiguration : IEntityTypeConfiguration<InboxReceiptEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InboxReceiptEntity> builder)
    {
        builder.ToTable(WorkDatabase.InboxReceipts, WorkDatabase.Schema);
        builder.HasKey(static receipt => receipt.MessageId);

        builder.Property(static receipt => receipt.MessageId)
            .HasColumnName("message_id")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static receipt => receipt.ClaimedAt)
            .HasColumnName("claimed_at")
            .IsRequired();
    }
}
