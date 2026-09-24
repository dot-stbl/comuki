using System.Collections.Concurrent;
using Comuki.Modules.Projects.Application.Settings.DistributedCache;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Comuki.Modules.Projects.Application.Settings.Cache;

/// <summary>
/// In-process snapshot cache of per-project settings (singleton — the
/// shared store every scope sees). Entries carry a short absolute TTL so a
/// dead refresher degrades to "not cached" instead of serving stale
/// values forever; <see cref="Refresh"/> replaces the entry AND fires the
/// per-project change token, <see cref="Warm"/> only fills the entry
/// (read-path fills must not look like changes). Pure memory — the
/// infrastructure decides what to warm and refresh. Used in single-replica
/// deployments and tests; a multi-replica deployment with <c>Redis:Enabled</c>
/// set swaps to <see cref="DistributedProjectSettingsCache"/> instead (same
/// <see cref="IProjectSettingsSnapshotCache"/> contract).
/// </summary>
/// <param name="cache">Backing in-process cache (framework-managed eviction, no persistence).</param>
public sealed class ProjectSettingsCache(IMemoryCache cache) : IProjectSettingsSnapshotCache
{
    /// <summary>Upper bound on snapshot staleness; the refresher re-arms entries roughly twice per TTL.</summary>
    public static readonly TimeSpan EntryTtl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<ProjectId, CancellationTokenSource> changeTokens = new();

    /// <inheritdoc />
    public ProjectSettings? Get(ProjectId projectId)
    {
        return cache.TryGetValue<ProjectSettings>(SettingsCacheKeys.Key(projectId), out var settings)
            ? settings
            : null;
    }

    /// <inheritdoc />
    public void Warm(ProjectSettings settings)
    {
        cache.Set(SettingsCacheKeys.Key(settings.ProjectId), settings, EntryTtl);
    }

    /// <inheritdoc />
    public void Refresh(ProjectSettings settings)
    {
        cache.Set(SettingsCacheKeys.Key(settings.ProjectId), settings, EntryTtl);
        NotifyChanged(settings.ProjectId);
    }

    /// <inheritdoc />
    public IChangeToken GetChangeToken(ProjectId projectId)
    {
        return new CancellationChangeToken(
            changeTokens.GetOrAdd(projectId, static _ => new CancellationTokenSource()).Token);
    }

    /// <summary>
    /// Fires (and retires) the current token of the project. The cancelled
    /// source is dropped rather than disposed — disposing a source while its
    /// callbacks run is racy, and an unregistered source holds no timers.
    /// </summary>
    /// <param name="projectId">Project whose pending change token should fire.</param>
    public void NotifyChanged(ProjectId projectId)
    {
        if (changeTokens.TryRemove(projectId, out var source))
        {
            source.Cancel();
        }
    }
}
