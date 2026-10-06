using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Verification;

/// <summary>
/// Tiny port the verification evaluator uses to read
/// <c>ProjectSettings.VerifyEnabled</c> without taking a dependency
/// on the Projects module (add-orchestra §3 — Coda,
/// <c>verification/spec.md</c> Requirement "ProjectSettings.VerifyEnabled
/// wires the verification path"). The engine cannot reference
/// <c>Comuki.Modules.Projects</c> per the layer rules; this port lives
/// in <c>Comuki.Shared.Contracts</c> and the host composes an adapter
/// over <c>IProjectSettingsStore.GetCached</c>.
/// </summary>
public interface IProjectVerificationSettings
{
    /// <summary>
    /// True when the project has verification enabled. Returns
    /// <see langword="false"/> when the project id is null (a
    /// cross-project global run — verification doesn't apply), when
    /// the project has no settings row yet (the row is created
    /// together with the project so this is a defensive default), or
    /// when the cached snapshot was never refreshed.
    /// </summary>
    /// <param name="projectId">Owning project; null for global runs.</param>
    public bool IsVerificationEnabled(ProjectId? projectId);
}
