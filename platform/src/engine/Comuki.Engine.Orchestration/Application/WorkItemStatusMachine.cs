using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.WorkItems;

namespace Comuki.Engine.Orchestration.Application;

/// <summary>
/// Work item status machine — the Application-facing seam over
/// <see cref="WorkItemTransitions"/>. Claim, heartbeat, reaper and stop
/// handlers validate transitions through this service; actor checks and
/// journal emission attach when the journal slice lands.
/// </summary>
public sealed class WorkItemStatusMachine
{
    private readonly IReadOnlyDictionary<WorkItemStatus, WorkItemStatus[]> allowed = WorkItemTransitions.Table;

    /// <summary>Returns true when <paramref name="from"/> -> <paramref name="to"/> is legal.</summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    public bool CanTransition(WorkItemStatus from, WorkItemStatus to) =>
        allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>Throws <see cref="InvalidOperationException"/> when the transition is illegal.</summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void EnsureTransition(WorkItemStatus from, WorkItemStatus to)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        throw new InvalidOperationException($"illegal work item transition {from} -> {to}");
    }

    /// <summary>All statuses reachable from <paramref name="from"/> in one hop.</summary>
    /// <param name="from"></param>
    public IReadOnlyCollection<WorkItemStatus> AllowedTargets(WorkItemStatus from) =>
        allowed.TryGetValue(from, out var targets) ? targets : [];
}
