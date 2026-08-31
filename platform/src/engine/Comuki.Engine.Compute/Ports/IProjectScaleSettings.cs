using Comuki.Engine.Compute.Settings;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Compute.Ports;

/// <summary>
/// Per-project scale settings: warm-idle floor, concurrency cap, idle TTL
/// and optional image/ref overrides. v0 ships the in-memory store; the
/// DB-backed store lands with the settings API slice (T2.6) behind the same
/// port.
/// </summary>
public interface IProjectScaleSettings
{
    /// <summary>Effective settings of the project — the per-project override, or the options defaults.</summary>
    /// <param name="projectId"></param>
    public ProjectScaleSettings Get(ProjectId projectId);

    /// <summary>Sets a per-project override (the settings API writes here once T2.6 lands).</summary>
    /// <param name="projectId"></param>
    /// <param name="settings"></param>
    public void Override(ProjectId projectId, ProjectScaleSettings settings);
}
