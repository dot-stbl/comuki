using Comuki.Modules.Work.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Work.Infrastructure.Persistence.Configurations;

/// <summary>OutboxWatermarks mapping: per-subscriber last-seen engine-outbox-id.</summary>
public sealed class OutboxWatermarkConfiguration : IEntityTypeConfiguration<OutboxWatermarkEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxWatermarkEntity> builder)
    {
        builder.ToTable(WorkDatabase.OutboxWatermarks, WorkDatabase.Schema);
        builder.HasKey(static wm => new { wm.Subscriber, wm.Type });

        builder.Property(static wm => wm.Subscriber)
            .HasColumnName("subscriber")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static wm => wm.Type)
            .HasColumnName("type")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static wm => wm.LastSeenId)
            .HasColumnName("last_seen_id")
            .IsRequired();

        builder.Property(static wm => wm.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
