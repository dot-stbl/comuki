using Comuki.Modules.Identity.Domain.ApiKeys;
using Comuki.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Identity.Infrastructure.Persistence.Configurations;

/// <summary>Api keys table mapping: unique public prefix, HMAC column, FK to the owner, optional tenant project id.</summary>
public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable(IdentityDatabase.ApiKeys, IdentityDatabase.Schema);
        builder.HasKey(static apiKey => apiKey.Id);

        builder.Property(static apiKey => apiKey.Id)
            .HasColumnName("id")
            .HasConversion(IdentityIdConverters.ApiKeyIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static apiKey => apiKey.UserId)
            .HasColumnName("user_id")
            .HasConversion(IdentityIdConverters.UserIdToUuid);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(static apiKey => apiKey.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(static apiKey => apiKey.Name)
            .HasColumnName("name")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static apiKey => apiKey.Prefix)
            .HasColumnName("prefix")
            .HasMaxLength(8)
            .IsFixedLength()
            .IsRequired();

        builder.Property(static apiKey => apiKey.KeyHmac)
            .HasColumnName("key_hmac")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        builder.Property(static apiKey => apiKey.TenantProjectId)
            .HasColumnName("tenant_project_id")
            .HasConversion(IdentityIdConverters.ProjectIdToUuid);

        builder.Property(static apiKey => apiKey.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static apiKey => apiKey.LastUsedAt)
            .HasColumnName("last_used_at");

        builder.Property(static apiKey => apiKey.RevokedAt)
            .HasColumnName("revoked_at");

        // A revoked key keeps its row (audit) — and its prefix stays burned:
        // the unique index is over ALL rows, not just active ones.
        builder.HasIndex(static apiKey => apiKey.Prefix)
            .IsUnique()
            .HasDatabaseName("ix_api_keys_prefix");

        // Tenant lookup is per-project (a fleet rotation finds every
        // scoped key for the project to revoke). Nulls are allowed —
        // legacy unscoped keys keep authenticating.
        builder.HasIndex(static apiKey => apiKey.TenantProjectId)
            .HasDatabaseName("ix_api_keys_tenant_project_id");
    }
}
