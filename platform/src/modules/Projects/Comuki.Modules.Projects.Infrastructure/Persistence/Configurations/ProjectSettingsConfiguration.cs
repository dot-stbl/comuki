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
        builder.ToTable(ProjectsTables.ProjectSettings);
        builder.HasKey(static settings => settings.ProjectId);

        builder.Property(static settings => settings.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(ProjectsIdConverters.ProjectIdToUuid)
            .ValueGeneratedNever();

        builder.HasOne<Project>()
            .WithOne()
            .HasForeignKey<ProjectSettings>(static settings => settings.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(static settings => settings.MinIdle)
            .HasColumnName("min_idle");

        builder.Property(static settings => settings.MaxConcurrent)
            .HasColumnName("max_concurrent");

        builder.Property(static settings => settings.IdleTtlSeconds)
            .HasColumnName("idle_ttl_seconds");

        builder.Property(static settings => settings.ApproveRequired)
            .HasColumnName("approve_required");

        builder.Property(static settings => settings.KnowledgeEnabled)
            .HasColumnName("knowledge_enabled");

        builder.Property(static settings => settings.VerifyEnabled)
            .HasColumnName("verify_enabled");

        builder.Property(static settings => settings.ProxyEnabled)
            .HasColumnName("proxy_enabled");

        builder.Property(static settings => settings.SoftBudgetUsdMicros)
            .HasColumnName("soft_budget_usd_micros");

        builder.Property(static settings => settings.HardBudgetUsdMicros)
            .HasColumnName("hard_budget_usd_micros");

        builder.Property(static settings => settings.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(static settings => settings.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();
    }
}
