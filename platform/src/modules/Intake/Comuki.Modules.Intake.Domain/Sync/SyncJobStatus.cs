namespace Comuki.Modules.Intake.Domain.Sync;

/// <summary>
/// Lifecycle of a sync-back job: enqueued by the run status bridge when
/// a claimed ticket's run reaches a terminal status, drained by the
/// sync-back worker into the tracker's transition API.
/// </summary>
public enum SyncJobStatus
{
    /// <summary>Waiting to be attempted or re-attempted.</summary>
    Pending,

    /// <summary>The tracker accepted the transition.</summary>
    Done,

    /// <summary>Attempts exhausted — parked for manual inspection.</summary>
    Failed,
}
