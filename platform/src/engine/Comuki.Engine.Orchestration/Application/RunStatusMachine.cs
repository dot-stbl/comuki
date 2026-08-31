using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;

namespace Comuki.Engine.Orchestration.Application;

/// <summary>
/// Run status machine — the Application-facing seam over
/// <see cref="RunTransitions"/>. Handlers validate transitions through this
/// service (not the raw table) so actor checks and journal emission can be
/// attached when the journal slice lands.
/// </summary>
public sealed class RunStatusMachine
{
    private readonly IReadOnlyDictionary<RunStatus, RunStatus[]> allowed = RunTransitions.Table;

    /// <summary>Returns true when <paramref name="from"/> -> <paramref name="to"/> is legal.</summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    public bool CanTransition(RunStatus from, RunStatus to) =>
        allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>Throws <see cref="InvalidOperationException"/> when the transition is illegal.</summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void EnsureTransition(RunStatus from, RunStatus to)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        throw new InvalidOperationException($"illegal run transition {from} -> {to}");
    }

    /// <summary>All statuses reachable from <paramref name="from"/> in one hop.</summary>
    /// <param name="from"></param>
    public IReadOnlyCollection<RunStatus> AllowedTargets(RunStatus from) =>
        allowed.TryGetValue(from, out var targets) ? targets : [];
}
