using Comuki.Modules.Work.Application.Ports.Engine;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Work;

/// <summary>
/// Host-side adapter that closes the Work-side
/// <see cref="IWorkCancelPort"/> over the engine's
/// <c>ICancelRunPort</c>. The Work module never imports
/// <c>Comuki.Modules.Integrations</c> or the engine — it depends
/// on this port; the adapter is the only place the host does the
/// cross-cutting wire-up (per <c>architecture.md</c> rule 4:
/// "Concretes are wired only at the composition root (host / entry
/// point)").
/// </summary>
public sealed class HostWorkEngineAdapter(ICancelRunPort cancelPort) : IWorkCancelPort
{
    /// <inheritdoc />
    public Task CancelAsync(RunId runId, string? reason = null, CancellationToken cancellationToken = default)
    {
        return cancelPort.CancelAsync(runId, reason, cancellationToken);
    }
}
