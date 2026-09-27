using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Application.Ports.Admission;

/// <summary>
/// The run-launch port — the module's only way to create a run. The
/// module never references the engine: the host composes the
/// implementation (mirroring <c>ChatRunStarter</c>), which writes the
/// run and its first work item through the orchestration context.
/// </summary>
public interface IRunLauncher
{
    /// <summary>
    /// Launches the run for an admitted ticket; returns the created run
    /// id. The connection (when present) carries the source settings the
    /// profile router reads — it is the only context the launcher has
    /// to choose between e.g. <c>pr-review</c> for an inbound PR and
    /// <c>implement</c> for a tracked issue. Native tickets pass null
    /// (no external tracker, no per-source override).
    /// <para>
    /// Idempotency contract (WS9, issue #87 — Orchestration-side
    /// guarantee): calling this twice with the same ticket identity —
    /// sequentially as a retry, or concurrently as a race — creates at
    /// most one Run. A losing / retried call returns the same Run id
    /// the winner created; it never throws and never produces a
    /// duplicate. The contract holds regardless of which caller invokes
    /// the port; <c>intake</c>'s own delivery-id / active-ticket locks
    /// are unchanged and remain the upstream defence against
    /// web-hook-level duplicates.
    /// </para>
    /// </summary>
    /// <param name="projectId"></param>
    /// <param name="connection"></param>
    /// <param name="ticket"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<RunId> LaunchAsync(
        ProjectId projectId,
        SourceConnection? connection,
        IncomingTicket ticket,
        CancellationToken cancellationToken = default);
}
