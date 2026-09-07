namespace Comuki.Engine.Orchestration.Domain.MergeQueue;

/// <summary>
/// Table-driven legal <see cref="MergeQueueStatus"/> transitions — single
/// source of truth shared by <see cref="MergeQueueEntry"/>'s aggregate
/// guard and any future Application-level status machine. Terminal
/// statuses (<see cref="MergeQueueStatus.Merged"/>,
/// <see cref="MergeQueueStatus.Abandoned"/>) have no outgoing edges;
/// <see cref="MergeQueueStatus.InProgress"/> -> <see cref="MergeQueueStatus.Pending"/>
/// is the release edge an operator uses when they step away from the
/// entry without completing it.
/// </summary>
public static class MergeQueueTransitions
{
    /// <summary>The transition table; the entry guard reads it.</summary>
    internal static readonly IReadOnlyDictionary<MergeQueueStatus, MergeQueueStatus[]> table =
        new Dictionary<MergeQueueStatus, MergeQueueStatus[]>
        {
            [MergeQueueStatus.Pending] = [MergeQueueStatus.InProgress, MergeQueueStatus.Abandoned],
            [MergeQueueStatus.InProgress] = [MergeQueueStatus.Merged, MergeQueueStatus.Abandoned, MergeQueueStatus.Pending],
            [MergeQueueStatus.Merged] = [],
            [MergeQueueStatus.Abandoned] = [],
        };

    /// <summary>Returns true when <paramref name="from"/> -> <paramref name="to"/> is a legal merge-queue transition.</summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    public static bool IsLegal(MergeQueueStatus from, MergeQueueStatus to)
    {
        return table.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>All statuses reachable from <paramref name="from"/> in one hop.</summary>
    /// <param name="from"></param>
    public static IReadOnlyCollection<MergeQueueStatus> TargetsFrom(MergeQueueStatus from)
    {
        return table.TryGetValue(from, out var targets) ? targets : [];
    }
}