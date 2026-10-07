using Comuki.Modules.Projects.Application.Ports;
using Comuki.Shared.Contracts.Verification;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Verification;

/// <summary>
/// Adapter that satisfies <see cref="IProjectVerificationSettings"/>
/// from the existing <see cref="IProjectSettingsStore"/> snapshot
/// cache (add-orchestra §3 — Coda, <c>verification/spec.md</c>
/// Requirement "ProjectSettings.VerifyEnabled wires the verification
/// path"). The engine never references the Projects module per the
/// layer rules; this thin shim lives in the host where the cross-
/// module composition is allowed.
/// </summary>
/// <param name="settingsStore">The cached per-project settings store.</param>
public sealed class ProjectVerificationSettingsAdapter(
    IProjectSettingsStore settingsStore) : IProjectVerificationSettings
{
    /// <inheritdoc />
    public bool IsVerificationEnabled(ProjectId? projectId)
    {
        if (projectId is null)
        {
            // Cross-project global runs are not project-scoped — the
            // gate skips (the spec: "when false, gates are skipped,
            // VerificationRecord rows are not written"). A null
            // project is the spec's "false" case.
            return false;
        }

        // The cache is warm from the existing ProjectSettingsCacheRefresher
        // worker (the field existed on the entity with zero consumers
        // before Coda — this is its first consumer). A null cache
        // hit means the project has no settings row yet, which is
        // also "verification not opted in".
        return settingsStore.GetCached(projectId.Value)?.VerifyEnabled ?? false;
    }
}
