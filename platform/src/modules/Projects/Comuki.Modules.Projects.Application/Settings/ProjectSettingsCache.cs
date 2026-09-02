using System.Collections.Concurrent;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Comuki.Modules.Projects.Application.Settings;

/// <summary>
/// In-process snapshot cache of per-project settings (singleton — the
/// shared store every scope sees). Entries carry a short absolute TTL so a
/// dead refresher degrades to "not cached" instead of serving stale
/// values forever; <see cref="Refresh"/> replaces the entry AND fires the
/// per-project change token, <see cref="Warm"/> only fills the entry
/// (read-path fills must not look like changes). Pure memory — the
/// infrastructure decides what to warm and refresh.
/// </summary>
/// <param name="cache"></param>
public sealed class ProjectSettingsCache(IMemoryCache cache)
{
    /// <summary>Upper bound on snapshot staleness; the refresher re-arms entries roughly twice per TTL.</summary>
    public static readonly TimeSpan EntryTtl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<ProjectId, CancellationTokenSource> changeTokens = new();

    /// <summary>Reads the cached snapshot; null when absent or expired.</summary>
    /// <param name="projectId"></param>
    /// <returns></returns>
    public ProjectSettings? Get(ProjectId projectId)
    {
        return cache.TryGetValue<ProjectSettings>(SettingsCacheKeys.Key(projectId), out var settings)
            ? settings
            : null;
    }

    /// <summary>Fills the cache entry without announcing a change (read-path fill).</summary>
    /// <param name="settings"></param>
    public void Warm(ProjectSettings settings)
    {
        cache.Set(SettingsCacheKeys.Key(settings.ProjectId), settings, EntryTtl);
    }

    /// <summary>Replaces the cache entry and fires the project's change token (write path).</summary>
    /// <param name="settings"></param>
    public void Refresh(ProjectSettings settings)
    {
        cache.Set(SettingsCacheKeys.Key(settings.ProjectId), settings, EntryTtl);
        NotifyChanged(settings.ProjectId);
    }

    /// <summary>Token that fires on the next <see cref="Refresh"/> of the project.</summary>
    /// <param name="projectId"></param>
    /// <returns></returns>
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
    /// <param name="projectId"></param>
    public void NotifyChanged(ProjectId projectId)
    {
        if (changeTokens.TryRemove(projectId, out var source))
        {
            source.Cancel();
        }
    }
}

/// <summary>Cache key scheme — one place, no format literals scattered.</summary>
file static class SettingsCacheKeys
{
    public static string Key(ProjectId projectId)
    {
        return $"projects:settings:{projectId.Value}";
    }
}
