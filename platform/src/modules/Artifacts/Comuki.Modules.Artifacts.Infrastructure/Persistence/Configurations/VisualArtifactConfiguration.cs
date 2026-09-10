using Comuki.Modules.Artifacts.Domain.VisualArtifacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Artifacts.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for the <c>visual_artifacts</c> metadata table (issue #51 slice 1).</summary>
public sealed class VisualArtifactConfiguration : IEntityTypeConfiguration<VisualArtifact>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VisualArtifact> builder)
    {
        builder.ToTable(ArtifactsDatabase.VisualArtifacts, ArtifactsDatabase.Schema);

        builder.HasKey(static artifact => artifact.Id);
        builder.Property(static artifact => artifact.Id).HasColumnName("id");
        builder.Property(static artifact => artifact.ProjectId).HasColumnName("project_id");
        builder.Property(static artifact => artifact.Version).HasColumnName("version");
        builder.Property(static artifact => artifact.Filename).HasColumnName("filename").HasMaxLength(512).IsRequired();
        builder.Property(static artifact => artifact.ContentType).HasColumnName("content_type").HasMaxLength(128).IsRequired();
        builder.Property(static artifact => artifact.SizeBytes).HasColumnName("size_bytes");
        builder.Property(static artifact => artifact.Title).HasColumnName("title").HasMaxLength(256);
        builder.Property(static artifact => artifact.CreatedAt).HasColumnName("created_at");
        builder.Property(static artifact => artifact.CreatedBy).HasColumnName("created_by").HasMaxLength(16).IsRequired();
        builder.Property(static artifact => artifact.RunId).HasColumnName("run_id");
        builder.Property(static artifact => artifact.WorkItemId).HasColumnName("work_item_id");
        builder.Property(static artifact => artifact.SessionId).HasColumnName("session_id");
        builder.Property(static artifact => artifact.TicketId).HasColumnName("ticket_id");

        // Object-axis indexes: the list endpoint filters by project + run,
        // the content proxy looks up by id (primary key), and the brain /
        // chat surfaces (slice 4) filter by session. Each index is small
        // and covers the documented read paths only.
        builder.HasIndex(static artifact => new { artifact.ProjectId, artifact.RunId })
            .HasDatabaseName("ix_visual_artifacts_project_run");
        builder.HasIndex(static artifact => new { artifact.ProjectId, artifact.WorkItemId })
            .HasDatabaseName("ix_visual_artifacts_project_work_item");
        builder.HasIndex(static artifact => new { artifact.ProjectId, artifact.SessionId })
            .HasDatabaseName("ix_visual_artifacts_project_session");
    }
}
