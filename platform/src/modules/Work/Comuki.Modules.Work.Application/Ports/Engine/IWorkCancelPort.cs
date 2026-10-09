using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Work.Application.Ports.Engine;

/// <summary>
/// Work-side cancel port — the Work module's only way to ask
/// the engine to cancel an in-flight Run. The host wires this
/// to the engine's <c>ICancelRunPort</c> (see
/// <c>Comuki.Host.Runs.HostCancelRunAdapter</c>) — Work
/// itself never references the engine's contract surface. The
/// call is what <c>WorkAttemptCancelledSubscriber</c> makes
/// when a <c>work.task.attempt-cancelled.v1</c> outbox row
/// lands: forward the RunId the Work side stamped on the
/// attempt, and the engine cancels + fences in its own
/// transaction (per Hidden F).
/// </summary>
public interface IWorkCancelPort
{
    /// <summary>Requests the engine to cancel <paramref name="runId"/>; idempotent on a missing or already-terminal run.</summary>
    public Task CancelAsync(RunId runId, string? reason = null, CancellationToken cancellationToken = default);
}
