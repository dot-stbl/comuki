using Comuki.Modules.Repositories.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Repositories.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>repository_credential_refs</c> mapping: one row per Repository (PK
/// + FK on <c>repository_id</c>, cascade-delete with the parent). The
/// integration reference is a bounded varchar (post-#88, opaque id).
/// <c>default_access</c> stores the <see cref="RepositoryAccess"/> smart-type
/// as a PascalCase string via <see cref="RepositoriesIdConverters.RepositoryAccessToString"/>;
/// a <c>varchar(16)</c> upper bound is more than enough for the
/// closed <c>External | Read | Write</c> set.
/// </summary>
public sealed class RepositoryCredentialRefConfiguration : IEntityTypeConfiguration<RepositoryCredentialRef>
{
    /// <summary>Upper bound of the integration reference column.</summary>
    public const int IntegrationRefMaxLength = 512;

    /// <summary>Upper bound of the default-access wire-form column.</summary>
    public const int DefaultAccessMaxLength = 16;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RepositoryCredentialRef> builder)
    {
        builder.ToTable(RepositoriesDatabase.RepositoryCredentialRefs, RepositoriesDatabase.Schema);
        builder.HasKey(static credential => credential.RepositoryId);

        builder.Property(static credential => credential.RepositoryId)
            .HasColumnName("repository_id")
            .HasConversion(RepositoriesIdConverters.RepositoryIdToUuid)
            .ValueGeneratedNever();

        builder.HasOne<Repository>()
            .WithMany()
            .HasForeignKey(static credential => credential.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(static credential => credential.IntegrationRef)
            .HasColumnName("integration_ref")
            .HasMaxLength(IntegrationRefMaxLength)
            .IsRequired();

        builder.Property(static credential => credential.DefaultAccess)
            .HasColumnName("default_access")
            .HasMaxLength(DefaultAccessMaxLength)
            .HasConversion(RepositoriesIdConverters.RepositoryAccessToString)
            .IsRequired();

        builder.Property(static credential => credential.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static credential => credential.UpdatedAt)
            .HasColumnName("updated_at");
    }
}
