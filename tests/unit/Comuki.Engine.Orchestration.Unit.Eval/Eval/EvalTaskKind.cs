namespace Comuki.Engine.Orchestration.Unit.Eval;

/// <summary>
/// Which engine aggregate the golden task exercises. The runner dispatches
/// on this to choose the right command surface (run-status vs work-item
/// lease + status transitions).
/// </summary>
public enum EvalTaskKind
{
    /// <summary>Operates on a <c>Run</c> aggregate — drives <see cref="Comuki.Engine.Orchestration.Domain.Runs.RunStatus"/> transitions.</summary>
    Run = 0,

    /// <summary>Operates on a <c>WorkItem</c> aggregate — drives <see cref="Comuki.Engine.Orchestration.Domain.WorkItemStatus"/> transitions plus lease ops.</summary>
    WorkItem = 1,
}
