using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Projects.Infrastructure.Persistence.Configurations;

/// <summary>
/// Project settings table mapping: project_id primary key (uuid, FK to
/// projects with cascade), scale columns, feature flags, and
/// <c>version</c> as an EF concurrency token so a lost update between two
/// concurrent writers is refused by the database, not by luck.
/// </summary>
public sealed class ProjectSettingsConfiguration : IEntityTypeConfiguration<ProjectSettings>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProjectSettings> builder)
    {
        _ = builder.ToTable(ProjectsTables.ProjectSettings);
        _ = builder.HasKey(static settings => settings.ProjectId);

        _ = builder.Property(static settings => settings.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(ProjectsIdConverters.ProjectIdToUuid)
            .ValueGeneratedNever();

        _ = builder.HasOne<Project>()
            .WithOne()
            .HasForeignKey<ProjectSettings>(static settings => settings.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.Property(static settings => settings.MinIdle)
            .HasColumnName("min_idle");

        _ = builder.Property(static settings => settings.MaxConcurrent)
            .HasColumnName("max_concurrent");

        _ = builder.Property(static settings => settings.IdleTtlSeconds)
            .HasColumnName("idle_ttl_seconds");

        _ = builder.Property(static settings => settings.ApproveRequired)
            .HasColumnName("approve_required");

        _ = builder.Property(static settings => settings.KnowledgeEnabled)
            .HasColumnName("knowledge_enabled");

        _ = builder.Property(static settings => settings.VerifyEnabled)
            .HasColumnName("verify_enabled");

        _ = builder.Property(static settings => settings.ProxyEnabled)
            .HasColumnName("proxy_enabled");

        _ = builder.Property(static settings => settings.UpdatedAt)
            .HasColumnName("updated_at");

        _ = builder.Property(static settings => settings.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();
    }
}
