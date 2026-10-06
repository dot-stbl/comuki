using System.Text.Json;
using Comuki.Engine.Orchestration.Domain.Verification;
using Comuki.Shared.Contracts.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>verifications</c> table mapping (add-orchestra §3 — Coda,
/// <c>verification/spec.md</c> Requirement "VerificationRecord is a
/// per-WorkItem sibling table"). The unique index on
/// <c>(work_item_id, gate_name)</c> is the re-evaluation contract — the
/// store's upsert re-uses the row instead of inserting a duplicate
/// (Requirement "Per-gate uniqueness"). Evidence refs are a
/// <c>jsonb</c> array; the verdict is the smart-type's wire format.
/// Snake_case naming is applied by the shared options recipe
/// (<see cref="OrchestrationDbContext.ApplyOptions"/>); explicit
/// column names keep the migration snapshot stable.
/// </summary>
public sealed class VerificationRecordConfiguration : IEntityTypeConfiguration<VerificationRecord>
{
    /// <summary>jsonb &lt;-&gt; <see cref="IReadOnlyList{T}"/> bridge. System.Text.Json because the engine has no pre-existing converter for this shape.</summary>
    private static readonly ValueConverter<IReadOnlyList<GateEvidenceRef>, string> evidenceRefsConverter =
        new(
            static refs => JsonSerializer.Serialize(refs, JsonSerializerOptions.Web),
            static json => JsonSerializer.Deserialize<List<GateEvidenceRef>>(json, JsonSerializerOptions.Web) ?? new List<GateEvidenceRef>());

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VerificationRecord> builder)
    {
        builder.ToTable(OrchestrationDatabase.Verifications, OrchestrationDatabase.Schema);
        builder.HasKey(static record => record.Id);

        builder.Property(static record => record.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(static record => record.WorkItemId)
            .HasColumnName("work_item_id");

        builder.Property(static record => record.GateName)
            .HasColumnName("gate_name")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static record => record.Verdict)
            .HasColumnName("verdict")
            .HasConversion(VerificationSmartTypeConverters.GateVerdictToString)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static record => record.EvidenceRefs)
            .HasColumnName("evidence_refs")
            .HasColumnType("jsonb")
            .HasConversion(evidenceRefsConverter, VerificationSmartTypeConverters.GateEvidenceRefsComparer)
            .IsRequired();

        builder.Property(static record => record.EvaluatedAt)
            .HasColumnName("evaluated_at")
            .IsRequired();

        builder.Property(static record => record.Evaluator)
            .HasColumnName("evaluator")
            .HasMaxLength(128)
            .IsRequired();

        // Unique index on (work_item_id, gate_name): the re-evaluation
        // contract — a re-evaluation produces an updated row, never a
        // duplicate. The upsert lives in VerificationRecordStoreEf.
        builder.HasIndex(static record => new { record.WorkItemId, record.GateName })
            .IsUnique()
            .HasDatabaseName("ux_verifications_work_item_id_gate_name");

        // The verification view's per-run scan: read every record for
        // the run's work items, newest first. Partial index not used
        // here because the view reads across all work items.
        builder.HasIndex(static record => record.WorkItemId)
            .HasDatabaseName("ix_verifications_work_item_id");
    }
}
