using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Mcp;

/// <summary>
/// <see cref="IWorkerProjectResolver"/> over the orchestration schema: the
/// newest running work item leased by the worker, joined to its run's
/// project. Runs as a platform-system consumer — the same posture as the
/// worker REST / gRPC surfaces — because a worker token is not an RBAC
/// subject and the subject-scope query filters must not confine the
/// lookup.
/// </summary>
/// <param name="db"></param>
/// <param name="scopeAccessor"></param>
public sealed class OrchestrationWorkerProjectResolver(
    OrchestrationDbContext db,
    ISubjectScopeAccessor scopeAccessor) : IWorkerProjectResolver
{
    /// <inheritdoc />
    public async Task<Guid?> ResolveProjectAsync(WorkerId workerId, CancellationToken cancellationToken = default)
    {
        using var systemScope = scopeAccessor.AsSystem("worker-runtime");

        // The projection is cast to Guid? on purpose: a value-typed
        // FirstOrDefault would answer Guid.Empty (the CLR default) for
        // "no row", indistinguishable from a real id — the nullable cast
        // keeps "no lease" a null on the wire.
        return await db.WorkItems
            .AsNoTracking()
            .Where(item => item.Status == WorkItemStatus.Running && item.LeasedBy == workerId)
            .OrderByDescending(item => item.HeartbeatAt)
            .Join(db.Runs, item => item.RunId, run => run.Id, (item, run) => (Guid?)run.ProjectId.Value)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
