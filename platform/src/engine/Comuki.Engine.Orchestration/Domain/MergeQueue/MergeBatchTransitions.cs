namespace Comuki.Engine.Orchestration.Domain.MergeQueue;

/// <summary>
/// Table-driven legal <see cref="MergeBatchStatus"/> transitions — the
/// batch's state is the aggregate of its entries' states. The
/// <see cref="MergeBatch"/> aggregate guard reads it.
/// </summary>
public static class MergeBatchTransitions
{
    /// <summary>The transition table.</summary>
    internal static readonly IReadOnlyDictionary<MergeBatchStatus, MergeBatchStatus[]> table =
        new Dictionary<MergeBatchStatus, MergeBatchStatus[]>
        {
            [MergeBatchStatus.Pending] = [MergeBatchStatus.InProgress, MergeBatchStatus.Abandoned],
            [MergeBatchStatus.InProgress] = [MergeBatchStatus.Merged, MergeBatchStatus.Abandoned],
            [MergeBatchStatus.Merged] = [],
            [MergeBatchStatus.Abandoned] = [],
        };

    /// <summary>Returns true when <paramref name="from"/> -> <paramref name="to"/> is a legal merge-batch transition.</summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    public static bool IsLegal(MergeBatchStatus from, MergeBatchStatus to)
    {
        return table.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>All statuses reachable from <paramref name="from"/> in one hop.</summary>
    /// <param name="from"></param>
    public static IReadOnlyCollection<MergeBatchStatus> TargetsFrom(MergeBatchStatus from)
    {
        return table.TryGetValue(from, out var targets) ? targets : [];
    }
}
