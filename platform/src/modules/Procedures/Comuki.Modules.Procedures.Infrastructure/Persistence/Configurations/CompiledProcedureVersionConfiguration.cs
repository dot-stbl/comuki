using Comuki.Modules.Procedures.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Procedures.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for the compiled procedure versions table:
/// snake_case columns, PK on the content-addressed version_id, and an
/// index on (project_id, procedure_key) for the "list versions" query.
/// </summary>
public sealed class CompiledProcedureVersionConfiguration : IEntityTypeConfiguration<CompiledProcedureVersionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CompiledProcedureVersionEntity> builder)
    {
        builder.ToTable(ProceduresDatabase.Tables.CompiledProcedureVersions, ProceduresDatabase.Schema);
        builder.HasKey(static entity => entity.VersionId);

        builder.Property(static entity => entity.VersionId)
            .HasColumnName("version_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(static entity => entity.ProjectId)
            .HasColumnName("project_id");

        builder.Property(static entity => entity.ProcedureKey)
            .HasColumnName("procedure_key")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(static entity => entity.CatalogVersion)
            .HasColumnName("catalog_version")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(static entity => entity.SourceRef)
            .HasColumnName("source_ref")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(static entity => entity.GraphJson)
            .HasColumnName("graph_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(static entity => entity.CreatedAt)
            .HasColumnName("created_at");

        builder.HasIndex(static entity => new { entity.ProjectId, entity.ProcedureKey })
            .HasDatabaseName("ix_compiled_procedure_versions_project_procedure");
    }
}
