using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Infrastructure.Leases;

/// <summary>
/// One reaped work item: what the reaper did with its expired lease.
/// <see cref="MarkedFailed"/> is true when the attempt budget was exhausted
/// (Running -> Failed) and false for a requeue (Running -> Queued).
/// </summary>
/// <param name="WorkItemId"></param>
/// <param name="RunId"></param>
/// <param name="Attempt"></param>
/// <param name="MarkedFailed"></param>
public sealed record ReapedLease(Guid WorkItemId, RunId RunId, int Attempt, bool MarkedFailed);
