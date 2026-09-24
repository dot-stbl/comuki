using System.Text.Json;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;

/// <summary>
/// Merge-batch table mapping: uuid id, bounded name + PR URL list (jsonb),
/// status as a short string, and the four lifecycle timestamps with the
/// optional abandon reason. Snake_case naming is applied by
/// <c>UseSnakeCaseNamingConvention</c> at the context level; explicit
/// column names keep the migration snapshot stable. The PR URL list is the
/// only reference-type collection on this aggregate — the
/// <c>pullRequestUrlsConverter</c> + <c>pullRequestUrlsComparer</c> pair lets
/// EF store it as a single jsonb string and still recognise
/// in-place edits without change-tracker noise (sequence-equal Equals,
/// aggregate hash, snapshot snapshot).
/// </summary>
public sealed class MergeBatchConfiguration : IEntityTypeConfiguration<MergeBatch>
{
    /// <summary>jsonb &lt;-&gt; <see cref="IReadOnlyList{T}"/> bridge. System.Text.Json because the engine has no pre-existing converter for this shape.</summary>
    private static readonly ValueConverter<IReadOnlyList<string>, string> pullRequestUrlsConverter =
        new(
            static urls => JsonSerializer.Serialize(urls, JsonSerializerOptions.Web),
            static json => JsonSerializer.Deserialize<List<string>>(json, JsonSerializerOptions.Web) ?? new List<string>());

    /// <summary>Change-tracker comparer — sequence-equal, snapshot hash. Keeps EF from mis-detecting "changed" on reference-stable lists.</summary>
    private static readonly ValueComparer<IReadOnlyList<string>> pullRequestUrlsComparer =
        new(
            equalsExpression: static (left, right) => ReferenceEquals(left, right)
                || (left != null && right != null && left.SequenceEqual(right)),
            hashCodeExpression: static urls => urls.Aggregate(0, static (accumulator, url) => HashCode.Combine(accumulator, url.GetHashCode(StringComparison.Ordinal))),
            snapshotExpression: static urls => urls.ToList());

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MergeBatch> builder)
    {
        builder.ToTable(OrchestrationDatabase.MergeBatches, OrchestrationDatabase.Schema);
        builder.HasKey(static batch => batch.Id);

        builder.Property(static batch => batch.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(static batch => batch.Name)
            .HasColumnName("name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(static batch => batch.PullRequestUrls)
            .HasColumnName("pull_request_urls")
            .HasColumnType("jsonb")
            .HasConversion(pullRequestUrlsConverter, pullRequestUrlsComparer)
            .IsRequired();

        builder.Property(static batch => batch.Status)
            .HasColumnName("status")
            .HasConversion(SmartTypeConverters.MergeBatchStatusToString)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static batch => batch.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static batch => batch.MergedAt)
            .HasColumnName("merged_at");

        builder.Property(static batch => batch.AbandonedAt)
            .HasColumnName("abandoned_at");

        builder.Property(static batch => batch.AbandonedReason)
            .HasColumnName("abandoned_reason")
            .HasMaxLength(1024);

        // Batches are listed newest first within an optional status filter
        // (see IMergeBatchStore.ListAsync xmldoc), so the composite index
        // covers the exact (status, created_at) scan shape. Mirrors the
        // merge_queue ix but for the batch table's own sort key.
        builder.HasIndex(static batch => new { batch.Status, batch.CreatedAt })
            .HasDatabaseName("ix_merge_batches_status_created_at");
    }
}
