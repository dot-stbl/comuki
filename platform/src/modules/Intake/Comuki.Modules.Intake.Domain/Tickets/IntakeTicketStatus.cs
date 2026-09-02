namespace Comuki.Modules.Intake.Domain.Tickets;

/// <summary>
/// Lifecycle of an intake ticket. <see cref="Pending"/> and
/// <see cref="Claimed"/> are the "active" statuses — the partial unique
/// index on <c>(project_id, source, external_id)</c> over exactly these
/// two is the one-live-run-per-issue lock (one issue = one active run;
/// the lock releases when the bridge moves the ticket to
/// <see cref="Done"/> after its run reached a terminal status).
/// </summary>
public enum IntakeTicketStatus
{
    /// <summary>Seen and admitted, waiting for a run (inbox mode or launch pending).</summary>
    Pending,

    /// <summary>A run was launched; the ticket holds its run id.</summary>
    Claimed,

    /// <summary>The ticket's run reached a terminal status; the lock is released.</summary>
    Done,

    /// <summary>Seen but filtered out by the admission rule; never runs.</summary>
    Dismissed,
}
