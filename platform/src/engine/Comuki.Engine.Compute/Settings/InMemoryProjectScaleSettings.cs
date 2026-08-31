using System.Collections.Concurrent;
using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Ports;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Compute.Settings;

/// <summary>
/// In-memory <see cref="IProjectScaleSettings"/>: defaults come from
/// <see cref="ScaleSupervisorOptions"/>, per-project overrides live in
/// process memory. The DB-backed store lands with the settings API slice
/// (T2.6) behind the same port; until then overrides vanish on restart.
/// </summary>
/// <param name="scaleOptions"></param>
public sealed class InMemoryProjectScaleSettings(IOptions<ScaleSupervisorOptions> scaleOptions) : IProjectScaleSettings
{
    private readonly ConcurrentDictionary<ProjectId, ProjectScaleSettings> overrides = new();

    /// <inheritdoc />
    public ProjectScaleSettings Get(ProjectId projectId)
    {
        return overrides.TryGetValue(projectId, out var settings)
            ? settings
            : new ProjectScaleSettings(
                scaleOptions.Value.MinIdle,
                scaleOptions.Value.MaxConcurrent,
                scaleOptions.Value.IdleTtl);
    }

    /// <inheritdoc />
    public void Override(ProjectId projectId, ProjectScaleSettings settings)
    {
        overrides[projectId] = settings;
    }
}
