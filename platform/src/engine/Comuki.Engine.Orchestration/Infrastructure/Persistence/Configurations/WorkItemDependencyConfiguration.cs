using Comuki.Engine.Orchestration.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;

/// <summary>Plan DAG edges: composite key, both sides cascade with the work item.</summary>
public sealed class WorkItemDependencyConfiguration : IEntityTypeConfiguration<WorkItemDependency>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkItemDependency> builder)
    {
        _ = builder.ToTable(OrchestrationTables.WorkItemDependencies);
        _ = builder.HasKey(static dependency => new { dependency.WorkItemId, dependency.DependsOnWorkItemId });

        _ = builder.Property(static dependency => dependency.WorkItemId)
            .HasColumnName("work_item_id");

        _ = builder.Property(static dependency => dependency.DependsOnWorkItemId)
            .HasColumnName("depends_on_work_item_id");

        _ = builder.HasOne<WorkItem>()
            .WithMany()
            .HasForeignKey(static dependency => dependency.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.HasOne<WorkItem>()
            .WithMany()
            .HasForeignKey(static dependency => dependency.DependsOnWorkItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
