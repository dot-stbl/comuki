using Comuki.Modules.Artifacts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Artifacts.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for the <c>run_bundles</c> bookkeeping table.</summary>
public sealed class RunBundleConfiguration : IEntityTypeConfiguration<RunArtifactBundle>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RunArtifactBundle> builder)
    {
        builder.ToTable(ArtifactsDatabase.RunBundles, ArtifactsDatabase.Schema);

        builder.HasKey(static bundle => bundle.RunId);
        builder.Property(static bundle => bundle.RunId).HasColumnName("run_id");
        builder.Property(static bundle => bundle.ProjectId).HasColumnName("project_id");
        builder.Property(static bundle => bundle.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(static bundle => bundle.UploadedAt).HasColumnName("uploaded_at");
        builder.Property(static bundle => bundle.ObjectCount).HasColumnName("object_count");
    }
}
