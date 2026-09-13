using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Workers.Read;

/// <summary>
/// Read-side query behind <c>GET /api/v1/workers</c> and
/// <c>GET /api/v1/workers/{workerId}</c>. The worker registry is derived,
/// never stored: the EF derivation lives in
/// <see cref="WorkersReadQuery"/> (no private business logic —
/// <c>code-shape.md</c> §1a); this handler owns normalization, paging
/// and the by-id lookup.
/// </summary>
/// <param name="query">Derived-workers EF query.</param>
public sealed class WorkersReadHandler(WorkersReadQuery query)
{
    /// <summary>Pages the derived workers: every live lease first (newest heartbeat), then journal-idle workers by recency.</summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, clamped to [1, 100].</param>
    /// <param name="cancellationToken">Cooperative cancellation for the derivation queries.</param>
    public async Task<WorkersPage> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedSize = Math.Clamp(pageSize, 1, 100);

        var derivation = await query.DeriveAsync(normalizedPage, normalizedSize, singleWorker: null, cancellationToken);
        return new WorkersPage(derivation.Rows, normalizedPage, normalizedSize, derivation.Total);
    }

    /// <summary>Returns one derived worker, or null when no live lease and no recent claim names them.</summary>
    /// <param name="workerId">Worker to read.</param>
    /// <param name="cancellationToken">Cooperative cancellation for the derivation queries.</param>
    public async Task<WorkerView?> GetAsync(WorkerId workerId, CancellationToken cancellationToken = default)
    {
        var derivation = await query.DeriveAsync(page: 1, pageSize: 100, singleWorker: workerId, cancellationToken);
        return derivation.Rows.SingleOrDefault(worker => worker.WorkerId == workerId.Value);
    }
}
