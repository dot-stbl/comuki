using Comuki.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Identity.Infrastructure.Persistence.Configurations;

/// <summary>Oidc links table mapping: unique (provider, sub).</summary>
public sealed class OidcLinkConfiguration : IEntityTypeConfiguration<OidcLink>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OidcLink> builder)
    {
        builder.ToTable(IdentityDatabase.OidcLinks, IdentityDatabase.Schema);
        builder.HasKey(static link => link.Id);

        builder.Property(static link => link.Id)
            .HasColumnName("id")
            .HasConversion(IdentityIdConverters.OidcLinkIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static link => link.UserId)
            .HasColumnName("user_id")
            .HasConversion(IdentityIdConverters.UserIdToUuid);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(static link => link.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(static link => link.Provider)
            .HasColumnName("provider")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(static link => link.Subject)
            .HasColumnName("sub")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(static link => link.CreatedAt)
            .HasColumnName("created_at");

        builder.HasIndex(static link => new { link.Provider, link.Subject })
            .IsUnique()
            .HasDatabaseName("ix_oidc_links_provider_sub");
    }
}
