using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Runs;

/// <summary>
/// The single shared resolver for the runId → WorkItem → LeasedBy → WorkerId
/// read path. Per the <c>add-orchestra</c> design (D3) and
/// <c>specs/session/spec.md</c> Requirement "RunId-to-ExecutionId resolver is
/// shared", the run-cancel endpoint and the run-steer endpoint MUST both
/// call through this seam — a per-endpoint resolver variant is forbidden.
/// <para>
/// The v1.x contract is the <see cref="WorkerId"/> the lease is currently
/// held under. The v2 cowork 11.1 slot/execution identity rides the same
/// seam later; the surface is <see cref="IExecutionIdResolver"/> so the
/// returning type can grow without renaming the seam.
/// </para>
/// <para>
/// Scoped — one orchestration <see cref="OrchestrationDbContext"/> per
/// resolution. The system scope is declared at the call site (every
/// consumer is a host operator endpoint, the orchestration context is
/// not subject-scoped).
/// </para>
/// </summary>
public interface IExecutionIdResolver
{
    /// <summary>
    /// Resolves <paramref name="runId"/> to its live <see cref="WorkerId"/>,
    /// or <c>null</c> when the run has no live lease (the run is queued /
    /// blocked / terminal, or its running work item's lease has expired and
    /// the reaper has not yet re-queued it). The implementation MUST do the
    /// resolution in a single read against the orchestration schema, the
    /// shape that <c>HostCancelRunAdapter</c> and the new steer endpoint
    /// both call.
    /// </summary>
    /// <param name="runId">Run to resolve.</param>
    /// <param name="cancellationToken"></param>
    public Task<WorkerId?> ResolveAsync(RunId runId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IExecutionIdResolver"/>: joins <c>runs</c> to
/// <c>work_items</c> in the orchestration schema, picking the single
/// Running work item whose <c>leased_by</c> is the worker we want to
/// reach. Multiple Running items under the same run answer
/// <c>null</c> defensively — that is a state corruption (the claim
/// contract is one-running-per-run) and the operator endpoint
/// surfaces it as the same "no live lease" outcome, identical to a
/// quiet worker. Terminal runs answer <c>null</c> directly — no
/// exception, the absence is the result.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="scopeAccessor">Ambient scope — declare system for the run.</param>
public sealed class ExecutionIdResolver(
    OrchestrationDbContext db,
    ISubjectScopeAccessor scopeAccessor) : IExecutionIdResolver
{
    /// <inheritdoc />
    public async Task<WorkerId?> ResolveAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        using var systemScope = scopeAccessor.AsSystem("runs-execution-id-resolver");

        // The seeded-lease shape is "one Running item per run" — the
        // claim contract. Two Running items under the same run is state
        // corruption; the resolver answers <c>null</c> defensively, the
        // operator endpoint surfaces it as the same "no live lease"
        // outcome, identical to a quiet worker.
        var leases = await db.WorkItems
            .AsNoTracking()
            .Where(item => item.RunId == runId
                && item.Status == WorkItemStatus.Running
                && item.LeasedBy != null)
            .Select(item => item.LeasedBy)
            .Take(2)
            .ToListAsync(cancellationToken);

        return leases.Count switch
        {
            0 => null,
            1 => leases[0],
            // Two Running items under the same run → corruption.
            _ => null,
        };
    }
}
