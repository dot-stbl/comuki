using Comuki.Modules.Repositories.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Repositories.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>repository_policies</c> mapping: one row per Repository (PK + FK on
/// <c>repository_id</c>, cascade-delete with the parent). The three rule
/// lists are native Postgres <c>text[]</c> — Npgsql maps <c>string[]</c>
/// straight onto them so the arrays stay queryable without a jsonb
/// round-trip. Empty arrays persist as the empty array (not null), so
/// the entity's invariant that "empty means no policy recorded" survives
/// a fetch round-trip.
/// </summary>
public sealed class RepositoryPolicyConfiguration : IEntityTypeConfiguration<RepositoryPolicy>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RepositoryPolicy> builder)
    {
        builder.ToTable(RepositoriesDatabase.RepositoryPolicies, RepositoriesDatabase.Schema);
        builder.HasKey(static policy => policy.RepositoryId);

        builder.Property(static policy => policy.RepositoryId)
            .HasColumnName("repository_id")
            .HasConversion(RepositoriesIdConverters.RepositoryIdToUuid)
            .ValueGeneratedNever();

        builder.HasOne<Repository>()
            .WithMany()
            .HasForeignKey(static policy => policy.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(static policy => policy.ProtectedBranches)
            .HasColumnName("protected_branches")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(static policy => policy.RequiredChecks)
            .HasColumnName("required_checks")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(static policy => policy.Approvers)
            .HasColumnName("approvers")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(static policy => policy.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static policy => policy.UpdatedAt)
            .HasColumnName("updated_at");
    }
}
