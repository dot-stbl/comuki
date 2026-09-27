using Comuki.Modules.Repositories.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Repositories.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>repositories</c> mapping: uuid key, normalized (host, url) pair
/// with the unique index that doubles as the dedup arbiter (concurrent
/// registrations with the same normalized identity race into the index,
/// not into a read-then-write check — the second insert fails with
/// <c>DbUpdateException</c> and the store returns the first row).
/// Default branch is bounded varchar; timestamps are <c>timestamptz</c>
/// via the Npgsql snake-case convention.
/// </summary>
public sealed class RepositoryConfiguration : IEntityTypeConfiguration<Repository>
{
    /// <summary>Upper bound of the clone url column — mirrors the longest git-host url the platform accepts.</summary>
    public const int UrlMaxLength = 2048;

    /// <summary>Upper bound of the host key column.</summary>
    public const int HostMaxLength = 256;

    /// <summary>Upper bound of the default branch column.</summary>
    public const int DefaultBranchMaxLength = 256;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Repository> builder)
    {
        builder.ToTable(RepositoriesDatabase.Repositories, RepositoriesDatabase.Schema);
        builder.HasKey(static repository => repository.Id);

        builder.Property(static repository => repository.Id)
            .HasColumnName("id")
            .HasConversion(RepositoriesIdConverters.RepositoryIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static repository => repository.Url)
            .HasColumnName("url")
            .HasMaxLength(UrlMaxLength)
            .IsRequired();

        builder.Property(static repository => repository.Host)
            .HasColumnName("host")
            .HasMaxLength(HostMaxLength)
            .IsRequired();

        builder.Property(static repository => repository.DefaultBranch)
            .HasColumnName("default_branch")
            .HasMaxLength(DefaultBranchMaxLength)
            .IsRequired();

        builder.Property(static repository => repository.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static repository => repository.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(static repository => new { repository.Host, repository.Url })
            .IsUnique()
            .HasDatabaseName("ux_repositories_host_url");
    }
}
