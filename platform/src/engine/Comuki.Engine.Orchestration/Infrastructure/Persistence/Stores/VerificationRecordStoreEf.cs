using System.Text.Json;
using Comuki.Engine.Orchestration.Application.Verification;
using Comuki.Engine.Orchestration.Domain.Verification;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Stores;

/// <summary>
/// Postgres implementation of <see cref="IVerificationRecordStore"/>
/// on top of <see cref="OrchestrationDbContext"/>. The upsert is a
/// raw <c>INSERT ... ON CONFLICT (work_item_id, gate_name) DO UPDATE</c>
/// — EF's change tracker is enough for the read side; the write path
/// needs the conflict-target upsert to honour the
/// "per-gate uniqueness" re-evaluation contract
/// (add-orchestra §3 — Coda, <c>verification/spec.md</c>).
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
public sealed class VerificationRecordStoreEf(OrchestrationDbContext db) : IVerificationRecordStore
{
    /// <inheritdoc />
    public async Task UpsertAsync(VerificationRecord record, CancellationToken cancellationToken = default)
    {
        // The single-insert upsert keeps the index unique and lets the
        // caller skip the round-trip "find, then update" two-step. The
        // column-list mirrors VerificationRecordConfiguration so a
        // schema drift surfaces at the upsert call site (the EF build
        // error fires long before this SQL ever runs in production).
        var parameters = new object[]
        {
            record.Id,
            record.WorkItemId,
            record.GateName,
            record.Verdict.Value,
            JsonSerializer.Serialize(record.EvidenceRefs, JsonSerializerOptions.Web),
            record.EvaluatedAt,
            record.Evaluator,
        };

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO orchestration.verifications
                (id, work_item_id, gate_name, verdict, evidence_refs, evaluated_at, evaluator)
            VALUES
                ({0}, {1}, {2}, {3}, {4}::jsonb, {5}, {6})
            ON CONFLICT (work_item_id, gate_name) DO UPDATE SET
                verdict = EXCLUDED.verdict,
                evidence_refs = EXCLUDED.evidence_refs,
                evaluated_at = EXCLUDED.evaluated_at,
                evaluator = EXCLUDED.evaluator
            """,
            parameters: parameters,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VerificationRecord>> ListByWorkItemAsync(
        Guid workItemId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.Verifications
            .AsNoTracking()
            .Where(record => record.WorkItemId == workItemId)
            .OrderByDescending(record => record.EvaluatedAt)
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VerificationRecord>> ListByWorkItemsAsync(
        IReadOnlyList<Guid> workItemIds,
        CancellationToken cancellationToken = default)
    {
        if (workItemIds.Count == 0)
        {
            return [];
        }

        var rows = await db.Verifications
            .AsNoTracking()
            .Where(record => workItemIds.Contains(record.WorkItemId))
            .OrderBy(record => record.WorkItemId)
            .ThenByDescending(record => record.EvaluatedAt)
            .ToListAsync(cancellationToken);

        return rows;
    }
}
