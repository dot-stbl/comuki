using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Verification;

/// <summary>
/// Per-call context the host hands to a
/// <see cref="IVerificationGateProvider"/>. The provider decides whether
/// the gate applies and, if so, evaluates; the context is immutable so
/// a provider can cache or memoize cheaply.
/// </summary>
/// <param name="WorkItemId">The work item the gate is asked to evaluate. Raw <c>Guid</c> — the engine's <c>WorkItem</c> stores the id as a <c>Guid</c> (the platform strong-types <c>RunId</c> / <c>ProjectId</c> but not <c>WorkItemId</c> because the DAG-edge, the lease row and the dependency table all share a <c>Guid</c> column).</param>
/// <param name="RunId">The run the work item belongs to — providers may join to the run row for project / profile / image context.</param>
/// <param name="ProjectId">Owning project — null for cross-project global runs.</param>
/// <param name="ProjectVerifyEnabled">
/// <c>ProjectSettings.VerifyEnabled</c> at the moment of evaluation.
/// When <see langword="false"/> the orchestrator never even calls a
/// provider (the <see cref="IVerificationGateProvider"/>'s
/// <c>AppliesTo</c> sees <see langword="false"/> and returns
/// <see cref="GateVerdict.Pending"/> — the convention is "no verdict
/// means no record"). The host still passes the value so a provider
/// can author its own out-of-band skip.
/// </param>
public sealed record VerificationContext(
    Guid WorkItemId,
    RunId RunId,
    ProjectId? ProjectId,
    bool ProjectVerifyEnabled);
