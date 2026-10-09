using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Exceptions;

namespace Comuki.Modules.Work.Application;

/// <summary>
/// Work task status machine — the Application-facing seam over
/// <see cref="WorkTaskTransitions"/>. The <c>AdmitTask</c>,
/// <c>DispatchRun</c>, <c>CancelAttempt</c>, <c>Resolve</c> and
/// <c>IngestRunTerminal</c> handlers validate transitions through
/// this service. The shape mirrors
/// <c>Comuki.Engine.Orchestration.Application.WorkItemStatusMachine</c>
/// — the engine's per-work-item machine — and the table under
/// <see cref="WorkTaskTransitions"/> is the single source of truth
/// the aggregate guard and this machine read in lockstep.
/// </summary>
public sealed class WorkTaskStatusMachine
{
    private readonly IReadOnlyDictionary<WorkTaskStatus, WorkTaskStatus[]> allowed = WorkTaskTransitions.table;

    /// <summary>Returns true when <paramref name="from"/> → <paramref name="to"/> is a legal Work task transition.</summary>
    public bool CanTransition(WorkTaskStatus from, WorkTaskStatus to)
    {
        return allowed.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>Throws <see cref="WorkTaskDomainException"/> when the transition is illegal.</summary>
    /// <exception cref="WorkTaskDomainException"></exception>
    public void EnsureTransition(WorkTaskStatus from, WorkTaskStatus to)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        throw new WorkTaskDomainException(
            WorkTaskErrorCodes.IllegalTransition,
            $"illegal work task transition {from} -> {to}");
    }

    /// <summary>All statuses reachable from <paramref name="from"/> in one hop.</summary>
    public IReadOnlyCollection<WorkTaskStatus> AllowedTargets(WorkTaskStatus from)
    {
        return allowed.TryGetValue(from, out var targets) ? targets : [];
    }
}
