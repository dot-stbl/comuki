namespace Comuki.Engine.Orchestration.Domain.Runs;

/// <summary>
/// Table-driven legal <see cref="RunStatus"/> transitions — single source of
/// truth shared by the <see cref="Run"/> aggregate guard and the Application
/// <c>RunStatusMachine</c>. Terminal statuses (<see cref="RunStatus.Succeeded"/>,
/// <see cref="RunStatus.Cancelled"/>) have no outgoing edges; <see cref="RunStatus.Failed"/>
/// can only be retried back to <see cref="RunStatus.Queued"/>.
/// </summary>
public static class RunTransitions
{
    /// <summary>The transition table; the machines and the aggregate guard read it.</summary>
    internal static readonly IReadOnlyDictionary<RunStatus, RunStatus[]> Table =
        new Dictionary<RunStatus, RunStatus[]>
        {
            [RunStatus.Queued] = [RunStatus.Waiting, RunStatus.Running, RunStatus.Failed, RunStatus.Cancelled, RunStatus.Escalated],
            [RunStatus.Waiting] = [RunStatus.Running, RunStatus.Failed, RunStatus.Cancelled, RunStatus.Escalated],
            [RunStatus.Running] = [RunStatus.Succeeded, RunStatus.Failed, RunStatus.Cancelled, RunStatus.Escalated],
            [RunStatus.Escalated] = [RunStatus.Running, RunStatus.Failed, RunStatus.Cancelled],
            [RunStatus.Failed] = [RunStatus.Queued],
            [RunStatus.Succeeded] = [],
            [RunStatus.Cancelled] = [],
        };

    /// <summary>Returns true when <paramref name="from"/> -> <paramref name="to"/> is a legal run transition.</summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    public static bool IsLegal(RunStatus from, RunStatus to) =>
        Table.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>All statuses reachable from <paramref name="from"/> in one hop.</summary>
    /// <param name="from"></param>
    public static IReadOnlyCollection<RunStatus> TargetsFrom(RunStatus from) =>
        Table.TryGetValue(from, out var targets) ? targets : [];
}
