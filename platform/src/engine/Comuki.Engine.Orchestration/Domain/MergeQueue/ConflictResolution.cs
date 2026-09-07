namespace Comuki.Engine.Orchestration.Domain.MergeQueue;

/// <summary>
/// How an operator intends to resolve conflicts when the branch lands.
/// Persisted as a hint alongside the entry so the dashboard can group
/// in-progress merges by strategy; the actual conflict detection +
/// resolution lives outside the engine (see issue #11 conflict-detection
/// follow-up) and this slice only records the operator's declared mode.
/// </summary>
public enum ConflictResolution
{
    /// <summary>No strategy declared yet.</summary>
    None = 0,

    /// <summary>Rebase the branch onto the target before merging.</summary>
    AutoRebase = 1,

    /// <summary>Operator will resolve conflicts by hand on the worktree.</summary>
    Manual = 2,

    /// <summary>Squash the branch into a single commit on the target.</summary>
    Squash = 3,
}
