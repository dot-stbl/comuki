using Comuki.Engine.Orchestration.Application.Verification;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Runs.Verification;

/// <summary>
/// Read-side handler behind <c>GET /api/v1/runs/{runId}/verification</c>
/// (add-orchestra §3 — Coda). Returns one <see cref="RunVerificationView"/>
/// per run — the flattened per-(work item, gate) verdict list — or
/// <c>null</c> when the run is out of scope (404 in the controller, by
/// the same convention as <see cref="GetRunDetailHandler"/>). A run
/// that has no verdicts yet reads as <see cref="RunVerificationView"/>
/// with an empty <c>Gates</c> list — the FE renders "no gates evaluated
/// yet" without a separate null path.
/// </summary>
/// <param name="db">Orchestration context — the run/work item + verification row reads share the same scoped unit-of-work.</param>
/// <param name="records">Per-scope <see cref="IVerificationRecordStore"/> for the verifications table.</param>
public sealed class GetRunVerificationViewHandler(
    OrchestrationDbContext db,
    IVerificationRecordStore records)
{
    /// <summary>
    /// Resolves the run's work items, then reads every per-(work item,
    /// gate) verdict and flattens it to a single newest-first list. The
    /// scope query filter on <c>Runs</c> / <c>WorkItems</c> gives the
    /// 404-by-design behaviour for out-of-scope runs.
    /// </summary>
    /// <param name="runId">Run to read.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public async Task<RunVerificationView?> GetAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        var runExists = await db.Runs
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Id == runId, cancellationToken);
        if (!runExists)
        {
            return null;
        }

        var workItemIds = await db.WorkItems
            .AsNoTracking()
            .Where(item => item.RunId == runId)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        if (workItemIds.Count == 0)
        {
            return new RunVerificationView(
                RunId: runId.Value,
                Gates: []);
        }

        var rows = await records.ListByWorkItemsAsync(workItemIds, cancellationToken);

        var gates = rows
            .Select(static row => new VerificationGateView(
                WorkItemId: row.WorkItemId,
                GateName: row.GateName,
                Verdict: row.Verdict.Value,
                EvidenceRefs: [.. row.EvidenceRefs.Select(static evidence => evidence.Uri.ToString())],
                EvaluatedAt: row.EvaluatedAt,
                Evaluator: row.Evaluator))
            .ToArray();

        return new RunVerificationView(
            RunId: runId.Value,
            Gates: gates);
    }
}
