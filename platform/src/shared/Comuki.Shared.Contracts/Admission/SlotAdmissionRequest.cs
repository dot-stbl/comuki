using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Admission;

/// <summary>
/// The input the orchestrator hands to <see cref="ISlotAdmissionEvaluator"/>:
/// the four behavior axes (role / toolchain / product rules / fleet) the
/// admission chokepoint composes. Kept separate from
/// the <c>SlotAdmission</c> contract in <c>Comuki.Engine.Compute.Admission</c> because the
/// request carries the editor's intent and the admission record carries
/// the resolved decision (admission id + denial code).
/// </summary>
/// <param name="ProjectId">Target repository this slot will run on.</param>
/// <param name="ProfileKey">Control-plane profile key this slot runs as (role axis).</param>
/// <param name="ProfilesRef">Pinned git ref of the profiles repo.</param>
/// <param name="EnvClass">Catalog id the project binds to (toolchain axis). Null or empty means <c>unconfirmed</c> — admission denies with <c>admission.env_unconfirmed</c>.</param>
/// <param name="IsolationClass">Isolation strength the slot requires (<c>trusted-process</c> | <c>strong</c>).</param>
/// <param name="SecretRefs">Stable ids of secrets the slot will read.</param>
/// <param name="RequestEdition">Edition coverage decision for the class/runtime (true = the active edition covers the request).</param>
public sealed record SlotAdmissionRequest(
    ProjectId ProjectId,
    string ProfileKey,
    string ProfilesRef,
    string? EnvClass,
    string IsolationClass,
    IReadOnlyList<string> SecretRefs,
    bool RequestEdition);

/// <summary>
/// Outcome of one <see cref="ISlotAdmissionEvaluator.EvaluateAsync"/> call.
/// A passed evaluation carries a populated <see cref="AdmissionId"/> the
/// caller stamps on the worker container (and on the journal entry); a
/// denied evaluation carries a populated <see cref="DenialCode"/> from
/// the <see cref="AdmissionCodes"/> set so the translator loop and the
/// operator surface branch on a stable typed value.
/// </summary>
/// <param name="Admitted">True when every ordered check passed.</param>
/// <param name="AdmissionId">Stable admission id (UUIDv7); null when <paramref name="Admitted"/> is false.</param>
/// <param name="DenialCode">Stable typed code of the denial (e.g. <see cref="AdmissionCodes.Publisher"/>); null when <paramref name="Admitted"/> is true.</param>
public sealed record SlotAdmissionResult(
    bool Admitted,
    Guid? AdmissionId,
    string? DenialCode);

/// <summary>
/// Stable typed codes for admission denials (worker-admission spec
/// §"Ordered typed evaluation"). The codes are the contract for the
/// translator loop, the journal payload, and the operator surface —
/// callers branch on them, never on <c>Exception.Message</c> or any
/// human-readable reason.
/// </summary>
public static class AdmissionCodes
{
    /// <summary>The repository has no confirmed <c>envClass</c>.</summary>
    public const string EnvUnconfirmed = "admission.env_unconfirmed";

    /// <summary>The publisher is not on the fleet allowlist.</summary>
    public const string Publisher = "admission.publisher";

    /// <summary>The fleet has no advertised slot of the requested class.</summary>
    public const string Capacity = "admission.capacity";

    /// <summary>The host cannot honor the requested isolation class.</summary>
    public const string Sandbox = "admission.sandbox";

    /// <summary>The active edition cannot cover the requested class/runtime.</summary>
    public const string Edition = "admission.edition";

    /// <summary>One or more secret refs are unresolvable under the worker subject.</summary>
    public const string Secrets = "admission.secrets";
}

/// <summary>
/// The ordered admission gate the orchestrator and the Translator call
/// before a coding-agent slot starts (worker-admission spec
/// §"SlotAdmission is the only start input"). The default implementation
/// lives in <c>Comuki.Engine.Compute.Admission</c>; this contract sits in
/// <c>Shared.Contracts</c> so the Translator (a worker-side host module)
/// can call the gate without pulling in the engine's Docker / Kubernetes
/// SDKs.
/// </summary>
public interface ISlotAdmissionEvaluator
{
    /// <summary>
    /// Runs the six ordered checks against <paramref name="request"/> and
    /// returns the first-deny outcome (or an admitted result). Stops at
    /// the first deny; never throws on a typed refusal — those are values.
    /// Throws only for unexpected infrastructure failures (DB unreachable,
    /// etc.) so the host's exception handler maps them to a 5xx ProblemDetails.
    /// </summary>
    /// <param name="request">The composed request — one per slot.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    public Task<SlotAdmissionResult> EvaluateAsync(
        SlotAdmissionRequest request,
        CancellationToken cancellationToken = default);
}
