using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Primitives;

namespace Comuki.Modules.Projects.Application.Ports;

/// <summary>
/// Persistence port for per-project settings with live-reload semantics
/// (issue #12 T4.8): every reader goes through this port, never through a
/// startup snapshot — settings changes apply without a restart. The DB
/// implementation keeps a short-TTL in-process snapshot cache
/// (<see cref="GetCached"/> — sync, memory-only, for consumers that cannot
/// await) and fires <see cref="GetChangeToken"/> per project on every
/// write, so subscribers observe mutations immediately.
/// </summary>
public interface IProjectSettingsStore
{
    /// <summary>Reads the settings row, refreshing the cached snapshot on the way.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<ProjectSettings?> FindAsync(ProjectId projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a mutated settings row. The caller mutates the entity loaded
    /// from <see cref="FindAsync"/> (its <c>Version</c> is expected-version +
    /// 1); a version mismatch — stale writer or concurrent writer race —
    /// throws <see cref="ProjectSettingsConflictException"/>.
    /// </summary>
    /// <param name="settings"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<ProjectSettings> SaveAsync(ProjectSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronous snapshot read from the in-process cache only — never
    /// touches the database. Null when the row is not cached (yet); callers
    /// fall back to their own defaults. Kept warm by the infrastructure
    /// refresher and by every write.
    /// </summary>
    /// <param name="projectId"></param>
    /// <returns></returns>
    public ProjectSettings? GetCached(ProjectId projectId);

    /// <summary>Change token that fires when the project's settings are written.</summary>
    /// <param name="projectId"></param>
    /// <returns></returns>
    public IChangeToken GetChangeToken(ProjectId projectId);
}
