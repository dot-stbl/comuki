using Comuki.Engine.Orchestration.Domain.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;

/// <summary>Runs table mapping: uuid ids, status as string, timestamptz.</summary>
public sealed class RunConfiguration : IEntityTypeConfiguration<Run>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Run> builder)
    {
        builder.ToTable(OrchestrationTables.Runs);
        builder.HasKey(static run => run.Id);

        builder.Property(static run => run.Id)
            .HasColumnName("id")
            .HasConversion(OrchestrationIdConverters.RunIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static run => run.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(OrchestrationIdConverters.ProjectIdToUuid);

        builder.Property(static run => run.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static run => run.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static run => run.UpdatedAt)
            .HasColumnName("updated_at");
    }
}
