using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;

/// <summary>
/// Host-composed port that maps a work item id to its owning run +
/// project when (and only when) the presenting worker holds the lease.
/// Lives in the application layer so the artifacts module never reaches
/// into the engine schema; the host wires an EF-backed implementation
/// over <c>OrchestrationDbContext</c>.
/// </summary>
public interface IWorkItemArtifactSource
{
    /// <summary>
    /// Returns the project + run the work item belongs to when the worker
    /// holds an unexpired lease on it. <c>null</c> otherwise — the caller
    /// translates that into HTTP 409 (the same code path Complete uses).
    /// </summary>
    /// <param name="workItemId">Work item id from the URL.</param>
    /// <param name="workerId">Worker id decoded from the bearer token.</param>
    /// <param name="now">Wall-clock for the lease check.</param>
    /// <param name="cancellationToken"></param>
    public Task<WorkItemOwnership?> FindOwnedAsync(
        Guid workItemId,
        WorkerId workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
