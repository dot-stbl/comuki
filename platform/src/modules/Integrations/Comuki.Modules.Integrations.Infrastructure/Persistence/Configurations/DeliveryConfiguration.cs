using Comuki.Modules.Integrations.Domain.Deliveries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Integrations.Infrastructure.Persistence.Configurations;

/// <summary>
/// deliveries mapping: the insert-first lock lives on the unique
/// (source, delivery_id) index.
/// </summary>
public sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable(IntegrationsDatabase.Deliveries, IntegrationsDatabase.Schema);
        builder.HasKey(static delivery => delivery.Id);

        builder.Property(static delivery => delivery.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(static delivery => delivery.Source)
            .HasColumnName("source")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(static delivery => delivery.DeliveryId)
            .HasColumnName("delivery_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(static delivery => delivery.Outcome)
            .HasColumnName("outcome")
            .HasMaxLength(32);

        builder.Property(static delivery => delivery.Detail)
            .HasColumnName("detail")
            .HasMaxLength(1024);

        builder.Property(static delivery => delivery.ReceivedAt)
            .HasColumnName("received_at");

        // Lock #1 — the same letter is never processed twice.
        builder.HasIndex(static delivery => new { delivery.Source, delivery.DeliveryId })
            .IsUnique()
            .HasDatabaseName("ux_deliveries_source_delivery");
    }
}
