using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Mcp;

/// <summary>
/// Resolves the project a worker's MCP calls are confined to: the project
/// of the run behind the work item the worker currently holds a lease on.
/// Server-side by design — a client-supplied projectId would be spoofable,
/// the lease ownership is not.
/// </summary>
public interface IWorkerProjectResolver
{
    /// <summary>
    /// Finds the project of the worker's running work item; null when the
    /// worker holds no running lease.
    /// </summary>
    /// <param name="workerId"></param>
    /// <param name="cancellationToken"></param>
    public Task<Guid?> ResolveProjectAsync(WorkerId workerId, CancellationToken cancellationToken = default);
}
