using Comuki.Modules.Intake.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Intake.Infrastructure.Persistence.Configurations;

/// <summary>admission_rules mapping: mode as string, filter as jsonb.</summary>
public sealed class AdmissionRuleConfiguration : IEntityTypeConfiguration<AdmissionRule>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AdmissionRule> builder)
    {
        builder.ToTable(IntakeTables.Rules);
        builder.HasKey(static rule => rule.Id);

        builder.Property(static rule => rule.Id)
            .HasColumnName("id")
            .HasConversion(IntakeIdConverters.RuleIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static rule => rule.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(IntakeIdConverters.ProjectIdToUuid);

        builder.Property(static rule => rule.Mode)
            .HasColumnName("mode")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static rule => rule.FilterJson)
            .HasColumnName("filter_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(static rule => rule.Enabled)
            .HasColumnName("enabled");

        builder.Property(static rule => rule.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static rule => rule.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(static rule => rule.ProjectId)
            .HasDatabaseName("ix_admission_rules_project");
    }
}
