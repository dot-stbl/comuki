using Comuki.Shared.Kernel.Harness;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Runs;

/// <summary>
/// Resolves the <see cref="IHarness"/> currently executing a run —
/// the seam the <see cref="HostSteerRunAdapter"/> reads to decide
/// whether the operator's steer lands authoritatively through the
/// bidi <see cref="Shared.Contracts.Grpc.TurnInput"/> channel
/// (add-orchestra Phase 1c) or falls back to the follow-up
/// <c>WorkItem</c> path (Phase 1a canonical).
/// <para>
/// The seam is its own port — the steering adapter does not call
/// the Translator's spawn seam directly (the runner is the
/// Translator's spawn detail, not the Host's decision surface).
/// The <see cref="IExecutionIdResolver"/> stays as the read-only
/// runId → <c>WorkerId</c> seam; the harness resolver is the
/// harness-side counterpart: given a run, return the harness whose
/// <see cref="HarnessCapabilities.LiveSession"/> the adapter reads.
/// </para>
/// <para>
/// Default implementation is a profile-keyed lookup against an
/// in-process harness registry (<see cref="HarnessRegistry"/>). The
/// full profile frontmatter parsing lands with the Phase 8 harness
/// catalog; Phase 1c only needs the surface — the test fakes
/// register a harness and the adapter reads it.
/// </para>
/// </summary>
public interface IRunHarnessResolver
{
    /// <summary>
    /// Resolves the harness currently executing <paramref name="runId"/>,
    /// or <c>null</c> when the run has no live execution (the run is
    /// queued / blocked / terminal, or no harness has been registered
    /// for the run's profile). The implementation MUST be a single
    /// read against the orchestration schema — the resolver is on
    /// the operator-endpoint critical path and any extra hop would
    /// show up in the steer's heartbeat-bound latency budget.
    /// </summary>
    /// <param name="runId">Run to resolve.</param>
    /// <param name="cancellationToken"></param>
    public Task<IHarness?> ResolveAsync(RunId runId, CancellationToken cancellationToken = default);
}
