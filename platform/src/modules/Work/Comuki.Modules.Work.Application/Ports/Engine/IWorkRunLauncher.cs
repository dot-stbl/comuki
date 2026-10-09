using Comuki.Shared.Contracts.Work;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Work.Application.Ports.Engine;

/// <summary>
/// Work-side run-launch port — the Work module's only way to
/// ask the engine to create a Run. The host wires this to the
/// engine's <c>IRunLauncher</c> (see
/// <c>Comuki.Host.Integration.IntegrationRunLauncher</c>) —
/// Work itself never imports <c>Comuki.Modules.Integrations</c>
/// or the engine. The dispatch contract is the
/// <see cref="WorkDispatchItem"/> envelope (per
/// <c>add-work-management/design.md</c> Decision 2); the
/// launcher's inbox-claim idempotency carries the dispatch
/// across host restarts (mirroring the WS9 admission-claim
/// pattern, see task #87).
/// </summary>
public interface IWorkRunLauncher
{
    /// <summary>Launches the next attempt of <paramref name="item"/>. Returns the created Run id, or the existing one if a prior claim already won.</summary>
    public Task<RunId> LaunchAsync(ProjectId projectId, WorkDispatchItem item, CancellationToken cancellationToken = default);
}
