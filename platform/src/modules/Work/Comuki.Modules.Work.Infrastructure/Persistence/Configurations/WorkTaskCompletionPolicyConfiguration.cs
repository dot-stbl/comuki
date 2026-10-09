using Comuki.Modules.Work.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Work.Infrastructure.Persistence.Configurations;

/// <summary>WorkTaskCompletionPolicies mapping: per-Task policy with versioned evidence contract.</summary>
public sealed class WorkTaskCompletionPolicyConfiguration : IEntityTypeConfiguration<WorkTaskCompletionPolicyEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkTaskCompletionPolicyEntity> builder)
    {
        builder.ToTable(WorkDatabase.WorkTaskCompletionPolicies, WorkDatabase.Schema);
        builder.HasKey(static policy => policy.Id);

        builder.Property(static policy => policy.Id)
            .HasColumnName("id");

        builder.Property(static policy => policy.TaskId)
            .HasColumnName("task_id")
            .IsRequired();

        builder.Property(static policy => policy.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(static policy => policy.PolicyKind)
            .HasColumnName("policy_kind")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(static policy => policy.EvidenceContractJson)
            .HasColumnName("evidence_contract")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(static policy => policy.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // The "latest policy version" lookup — newest version per Task.
        builder.HasIndex(static policy => new { policy.TaskId, policy.Version })
            .IsUnique()
            .HasDatabaseName("ux_work_task_completion_policies_task_version");
    }
}
