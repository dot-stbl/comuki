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
        builder.ToTable(IdentityDatabase.Users, IdentityDatabase.Schema);
        builder.HasKey(static user => user.Id);

        builder.Property(static user => user.Id)
            .HasColumnName("id")
            .HasConversion(IdentityIdConverters.UserIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(static user => user.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(static user => user.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(512);

        builder.Property(static user => user.TokensVersion)
            .HasColumnName("tokens_version");

        builder.Property(static user => user.Disabled)
            .HasColumnName("disabled");

        builder.Property(static user => user.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static user => user.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(static user => user.Email)
            .IsUnique()
            .HasDatabaseName("ix_users_email");
    }
}
