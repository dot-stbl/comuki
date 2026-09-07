namespace Comuki.Engine.Orchestration.Unit.Eval.Eval;

/// <summary>
/// A single operation the runner applies to the aggregate, in the order
/// declared by <see cref="EvalTask.Operations"/>. Each kind surfaces
/// only the operations that make sense for it — e.g. lease ops are
/// meaningful only for <see cref="EvalTaskKind.WorkItem"/>.
/// </summary>
public enum EvalAction
{
    /// <summary>Create the aggregate in its entry status. Always the first op.</summary>
    Create = 0,

    /// <summary>Apply a status transition. The target status is read from <see cref="EvalOperation.Status"/>.</summary>
    Transition = 1,

    /// <summary>(work-item) Assign a lease — moves Queued -> Running and stamps the lease columns.</summary>
    AssignLease = 2,

    /// <summary>(work-item) Extend the lease of a running, leased item.</summary>
    Heartbeat = 3,

    /// <summary>(work-item) Release the lease — moves Running -> Queued.</summary>
    ReleaseLease = 4,

    /// <summary>Apply the transition; expect it to throw. Used to drive the negative path.</summary>
    TransitionExpectFailure = 5,
}
