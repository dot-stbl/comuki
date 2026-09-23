using Comuki.Modules.Projects.Application.Settings.DistributedCache;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Primitives;

namespace Comuki.Modules.Projects.Application.Settings.Cache;

/// <summary>
/// Per-project settings snapshot cache abstraction. The live-reload store
/// (<c>IProjectSettingsStore</c>) reads from it, the refresher worker
/// writes through it, the compute adapter subscribes for change
/// notifications. Two implementations: the in-process
/// <see cref="ProjectSettingsCache"/> (default, single replica) and
/// <see cref="DistributedProjectSettingsCache"/> (Redis, multi-replica).
/// <c>ProjectsApplicationExtensions.AddProjectsApplication</c> picks one at
/// composition time based on whether
/// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// is registered.
/// </summary>
public interface IProjectSettingsSnapshotCache
{
    /// <summary>Reads the cached snapshot; null when absent or expired.</summary>
    /// <param name="projectId">Project the snapshot belongs to.</param>
    /// <returns>The cached row, or null on a cache miss.</returns>
    public ProjectSettings? Get(ProjectId projectId);

    /// <summary>Fills the cache entry without announcing a change (read-path fill).</summary>
    /// <param name="settings">Snapshot to store, keyed by its own <see cref="ProjectSettings.ProjectId"/>.</param>
    public void Warm(ProjectSettings settings);

    /// <summary>Replaces the cache entry and fires the project's change token (write path).</summary>
    /// <param name="settings">Snapshot to store, keyed by its own <see cref="ProjectSettings.ProjectId"/>.</param>
    public void Refresh(ProjectSettings settings);

    /// <summary>Token that fires on the next <see cref="Refresh"/> of the project.</summary>
    /// <param name="projectId">Project to watch.</param>
    /// <returns>A token whose <c>HasChanged</c> flips on the next <see cref="Refresh"/> call for this project.</returns>
    public IChangeToken GetChangeToken(ProjectId projectId);
}
