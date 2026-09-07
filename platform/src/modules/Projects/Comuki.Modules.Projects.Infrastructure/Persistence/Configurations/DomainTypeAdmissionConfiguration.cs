using Comuki.Modules.Projects.Domain.DomainTypes;
using Comuki.Modules.Projects.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Projects.Infrastructure.Persistence.Configurations;

/// <summary>
/// domain_type_admissions mapping: uuid key, uuid project FK (cascade with
/// the project), the domain-type key as bounded varchar, and both policy
/// lists as native Postgres <c>text[]</c> (Npgsql maps <c>string[]</c>
/// straight onto them — no jsonb round-trip, so the arrays stay queryable).
/// A unique index over (project_id, domain_type) is the arbiter of "one
/// policy per domain type": concurrent creates race into the index, not
/// into a read-then-write check.
/// </summary>
public sealed class DomainTypeAdmissionConfiguration : IEntityTypeConfiguration<DomainTypeAdmission>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DomainTypeAdmission> builder)
    {
        builder.ToTable(ProjectsDatabase.DomainTypeAdmissions, ProjectsDatabase.Schema);
        builder.HasKey(static admission => admission.Id);

        builder.Property(static admission => admission.Id)
            .HasColumnName("id")
            .HasConversion(ProjectsIdConverters.DomainTypeAdmissionIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static admission => admission.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(ProjectsIdConverters.ProjectIdToUuid);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(static admission => admission.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(static admission => admission.DomainType)
            .HasColumnName("domain_type")
            .HasMaxLength(DomainTypeAdmission.MaxDomainTypeLength)
            .IsRequired();

        builder.Property(static admission => admission.AllowedSources)
            .HasColumnName("allowed_sources")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(static admission => admission.DeniedReasons)
            .HasColumnName("denied_reasons")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(static admission => admission.Enabled)
            .HasColumnName("enabled");

        builder.Property(static admission => admission.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static admission => admission.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(static admission => new { admission.ProjectId, admission.DomainType })
            .IsUnique()
            .HasDatabaseName("ux_domain_type_admissions_project_domain");
    }
}
