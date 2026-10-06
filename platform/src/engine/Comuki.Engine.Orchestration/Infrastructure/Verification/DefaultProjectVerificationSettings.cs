using Comuki.Shared.Contracts.Verification;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Infrastructure.Verification;

/// <summary>
/// Engine-side default for <see cref="IProjectVerificationSettings"/>
/// (add-orchestra §3 — Coda, <c>verification/spec.md</c> Requirement
/// "ProjectSettings.VerifyEnabled wires the verification path"). The
/// engine owns the verification domain services; the port the
/// <c>VerificationEvaluationService</c> reads from lives in
/// <see cref="IProjectVerificationSettings"/> and the engine's
/// composition cannot reference the Projects module (the adapter that
/// turns the cached project-settings row into
/// <c>IsVerificationEnabled</c> lives in the host where the
/// cross-module composition is allowed).
/// </summary>
/// <remarks>
/// The default answers <see langword="false"/> for every project —
/// the spec's "verification not opted in" semantic. With this default
/// the engine composition validates end-to-end on its own (the
/// <c>DiComposition</c> unit test builds only the engine composition
/// and needs <c>WorkItemQueueEf</c> resolvable, and the evaluation
/// service in turn needs <see cref="IProjectVerificationSettings"/>).
/// In a real host composition the registration is overridden by the
/// adapter over <c>IProjectSettingsStore.GetCached</c> in
/// <c>Host.Verification.ProjectVerificationSettingsAdapter</c> — the
/// last singleton registration for the port wins. The default never
/// runs in production.
/// </remarks>
public sealed class DefaultProjectVerificationSettings : IProjectVerificationSettings
{
    /// <inheritdoc />
    public bool IsVerificationEnabled(ProjectId? projectId)
    {
        return false;
    }
}
