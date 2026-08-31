using Comuki.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Identity.Infrastructure.Persistence.Configurations;

/// <summary>Users table mapping: uuid id, unique lower-cased email, nullable password hash.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        _ = builder.ToTable(IdentityTables.Users);
        _ = builder.HasKey(static user => user.Id);

        _ = builder.Property(static user => user.Id)
            .HasColumnName("id")
            .HasConversion(IdentityIdConverters.UserIdToUuid)
            .ValueGeneratedNever();

        _ = builder.Property(static user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        _ = builder.Property(static user => user.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(256)
            .IsRequired();

        _ = builder.Property(static user => user.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(512);

        _ = builder.Property(static user => user.TokensVersion)
            .HasColumnName("tokens_version");

        _ = builder.Property(static user => user.Disabled)
            .HasColumnName("disabled");

        _ = builder.Property(static user => user.CreatedAt)
            .HasColumnName("created_at");

        _ = builder.Property(static user => user.UpdatedAt)
            .HasColumnName("updated_at");

        _ = builder.HasIndex(static user => user.Email)
            .IsUnique()
            .HasDatabaseName("ix_users_email");
    }
}
