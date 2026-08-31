namespace Comuki.Engine.Orchestration.Domain.WorkItems;

/// <summary>
/// Table-driven legal <see cref="WorkItemStatus"/> transitions — single source
/// of truth shared by the <see cref="WorkItem"/> aggregate guard and the
/// Application <c>WorkItemStatusMachine</c>. <see cref="WorkItemStatus.Running"/>
/// -> <see cref="WorkItemStatus.Queued"/> is the lease-expiry requeue edge the
/// reaper uses; <see cref="WorkItemStatus.Failed"/> -> <see cref="WorkItemStatus.Queued"/>
/// is the retry edge. Terminal statuses have no outgoing edges.
/// </summary>
public static class WorkItemTransitions
{
    /// <summary>The transition table; the machines and the aggregate guard read it.</summary>
    internal static readonly IReadOnlyDictionary<WorkItemStatus, WorkItemStatus[]> Table =
        new Dictionary<WorkItemStatus, WorkItemStatus[]>
        {
            [WorkItemStatus.Blocked] = [WorkItemStatus.Queued, WorkItemStatus.Failed, WorkItemStatus.Cancelled],
            [WorkItemStatus.Queued] = [WorkItemStatus.Running, WorkItemStatus.Failed, WorkItemStatus.Cancelled],
            [WorkItemStatus.Running] = [WorkItemStatus.Succeeded, WorkItemStatus.Failed, WorkItemStatus.Cancelled, WorkItemStatus.Queued],
            [WorkItemStatus.Failed] = [WorkItemStatus.Queued],
            [WorkItemStatus.Succeeded] = [],
            [WorkItemStatus.Cancelled] = [],
        };

    /// <summary>Returns true when <paramref name="from"/> -> <paramref name="to"/> is a legal work item transition.</summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    public static bool IsLegal(WorkItemStatus from, WorkItemStatus to) =>
        Table.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>All statuses reachable from <paramref name="from"/> in one hop.</summary>
    /// <param name="from"></param>
    public static IReadOnlyCollection<WorkItemStatus> TargetsFrom(WorkItemStatus from) =>
        Table.TryGetValue(from, out var targets) ? targets : [];
}
