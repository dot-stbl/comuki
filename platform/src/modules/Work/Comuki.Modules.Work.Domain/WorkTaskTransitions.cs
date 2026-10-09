namespace Comuki.Modules.Work.Domain;

/// <summary>
/// Table-driven legal <see cref="WorkTaskStatus"/> transitions — the
/// single source of truth shared by the <see cref="WorkTask"/>
/// aggregate guard and the Application <c>WorkTaskStatusMachine</c>
/// (added in task 2.3 of the change). The diagram is normative:
/// <c>Draft → Ready → Active ↔ Blocked → Resolved</c> with a side
/// exit <c>Cancelled</c> from any non-terminal status. Terminal
/// statuses (<see cref="WorkTaskStatus.Resolved"/>,
/// <see cref="WorkTaskStatus.Cancelled"/>) carry no outgoing edges.
/// </summary>
public static class WorkTaskTransitions
{
    /// <summary>The transition table; the aggregate guard and (later) the status machine read it.</summary>
    internal static readonly IReadOnlyDictionary<WorkTaskStatus, WorkTaskStatus[]> table =
        new Dictionary<WorkTaskStatus, WorkTaskStatus[]>
        {
            [WorkTaskStatus.Draft] = [WorkTaskStatus.Ready, WorkTaskStatus.Cancelled],
            [WorkTaskStatus.Ready] = [WorkTaskStatus.Active, WorkTaskStatus.Blocked, WorkTaskStatus.Cancelled],
            [WorkTaskStatus.Active] = [WorkTaskStatus.Blocked, WorkTaskStatus.Resolved, WorkTaskStatus.Cancelled],
            [WorkTaskStatus.Blocked] = [WorkTaskStatus.Ready, WorkTaskStatus.Resolved, WorkTaskStatus.Cancelled],
            [WorkTaskStatus.Resolved] = [],
            [WorkTaskStatus.Cancelled] = [],
        };

    /// <summary>Returns true when <paramref name="from"/> → <paramref name="to"/> is a legal Task transition.</summary>
    public static bool IsLegal(WorkTaskStatus from, WorkTaskStatus to)
    {
        return table.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>All statuses reachable from <paramref name="from"/> in one hop.</summary>
    public static IReadOnlyCollection<WorkTaskStatus> TargetsFrom(WorkTaskStatus from)
    {
        return table.TryGetValue(from, out var targets) ? targets : [];
    }
}
