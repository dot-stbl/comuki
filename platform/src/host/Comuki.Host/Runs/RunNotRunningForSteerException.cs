using Comuki.Engine.Orchestration.Domain;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Host.Runs;

/// <summary>
/// The steer endpoint was called on a run whose current status is
/// terminal or finalising (add-orchestra §1 — Baton, Phase 1a). The
/// follow-up WorkItem is never staged on a dead run, so the handler
/// throws this before <see cref="IExecutionIdResolver.ResolveAsync"/>
/// and before the follow-up factory.
/// </summary>
/// <remarks>
/// Maps to HTTP 409 <c>application/problem+json</c> with the stable
/// <c>code = run.not_running</c> extension, the run's current status
/// and the targeted decision (<c>"steer"</c>) in the same shape the
/// cancel/approve decisions emit — the operator reads one consistent
/// "the run cannot be steered right now" response across the three
/// decision endpoints.
/// </remarks>
/// <param name="current">Run's current status (PascalCase).</param>
public sealed class RunNotRunningForSteerException(RunStatus current) : DomainException(ErrorCode, $"run is in {current} and cannot be steered — follow-up is refused")
{
    private const string ErrorCode = "run.not_running";

    /// <summary>Current run status (PascalCase).</summary>
    public RunStatus Current { get; } = current;
}
