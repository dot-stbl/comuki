using System.Collections.Concurrent;
using System.Text.Json;
using Comuki.Modules.Projects.Application.Settings.Cache;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Primitives;

namespace Comuki.Modules.Projects.Application.Settings.DistributedCache;

/// <summary>
/// Redis-backed snapshot cache of per-project settings (singleton — the
/// shared store every Host replica sees). Values carry the same
/// <see cref="ProjectSettingsCache.EntryTtl"/> as the in-process cache, so
/// a dead refresher degrades to "not cached" rather than serving stale
/// values forever. <see cref="Refresh"/> replaces the value AND fires the
/// in-process change token for subscribers on THIS replica; cross-replica
/// change-token broadcasting is a separate slice (a Redis pub/sub channel
/// keyed by project id) — not needed yet, every replica runs its own
/// settings-cache refresher poll.
/// <para>
/// Reads and writes go through the sync overloads of
/// <see cref="IDistributedCache"/> — first-class members of the
/// abstraction; <c>Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache</c>
/// owns the async-over-sync bridging internally, so this class never
/// writes <c>GetAwaiter().GetResult()</c> in user code
/// (async-and-tasks.md §5).
/// </para>
/// </summary>
/// <param name="cache">Distributed cache abstraction — Redis-backed when <c>Redis:Enabled</c> is true.</param>
public sealed class DistributedProjectSettingsCache(IDistributedCache cache) : IProjectSettingsSnapshotCache
{
    private readonly ConcurrentDictionary<ProjectId, CancellationTokenSource> changeTokens = new();

    /// <inheritdoc />
    public ProjectSettings? Get(ProjectId projectId)
    {
        var bytes = cache.Get(SettingsCacheKeys.Key(projectId));
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        var entry = JsonSerializer.Deserialize<ProjectSettingsCacheEntry>(bytes, JsonSerializerOptions.Web);
        return entry is null ? null : ProjectSettingsCacheEntryMapper.ToSettings(entry);
    }

    /// <inheritdoc />
    public void Warm(ProjectSettings settings)
    {
        cache.Set(
            SettingsCacheKeys.Key(settings.ProjectId),
            JsonSerializer.SerializeToUtf8Bytes(ProjectSettingsCacheEntryMapper.ToEntry(settings), JsonSerializerOptions.Web),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ProjectSettingsCache.EntryTtl });
    }

    /// <inheritdoc />
    public void Refresh(ProjectSettings settings)
    {
        Warm(settings);
        NotifyChanged(settings.ProjectId);
    }

    /// <inheritdoc />
    public IChangeToken GetChangeToken(ProjectId projectId)
    {
        return new CancellationChangeToken(
            changeTokens.GetOrAdd(projectId, static _ => new CancellationTokenSource()).Token);
    }

    /// <summary>
    /// Fires (and retires) the current token of the project. Same
    /// disposal contract as <see cref="ProjectSettingsCache.NotifyChanged"/>
    /// — a cancelled source is dropped rather than disposed: disposing a
    /// source while its callbacks run is racy, and an unregistered source
    /// holds no timers.
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

/// <summary>
/// Wire shape of one <see cref="ProjectSettings"/> snapshot for the
/// distributed cache (JSON, UTF-8 bytes via
/// <see cref="JsonSerializerOptions.Web"/>). A flat, STJ-constructible
/// value bag — never the domain type itself, whose constructor stays
/// owned by EF Core; round-tripping a snapshot through an external store
/// is the mapper's job (<see cref="ProjectSettingsCacheEntryMapper"/>),
/// not the entity's. File-scoped: <see cref="DistributedProjectSettingsCache"/>
/// is its only consumer (class-layout-and-tooling.md §1a).
/// </summary>
file sealed record ProjectSettingsCacheEntry
{
    public required ProjectId ProjectId { get; init; }

    public required int MinIdle { get; init; }

    public required int MaxConcurrent { get; init; }

    public int? IdleTtlSeconds { get; init; }

    public required bool ApproveRequired { get; init; }

    public required bool KnowledgeEnabled { get; init; }

    public required bool VerifyEnabled { get; init; }

    public required bool ProxyEnabled { get; init; }

    public long? SoftBudgetUsdMicros { get; init; }

    public long? HardBudgetUsdMicros { get; init; }

    public required ProjectDomainType DomainType { get; init; }

    public string? CustomDomainTypesJson { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required int Version { get; init; }
}

/// <summary>
/// Pure conversion between <see cref="ProjectSettings"/> and its
/// distributed-cache wire shape (<see cref="ProjectSettingsCacheEntry"/>).
/// No I/O — <see cref="DistributedProjectSettingsCache"/> owns the actual
/// JSON (de)serialization and the <see cref="IDistributedCache"/> calls;
/// this class only shuffles fields (mapper.md). File-scoped: single
/// consumer, same file as the entry it converts.
/// </summary>
file static class ProjectSettingsCacheEntryMapper
{
    public static ProjectSettingsCacheEntry ToEntry(ProjectSettings settings)
    {
        return new ProjectSettingsCacheEntry
        {
            ProjectId = settings.ProjectId,
            MinIdle = settings.MinIdle,
            MaxConcurrent = settings.MaxConcurrent,
            IdleTtlSeconds = settings.IdleTtlSeconds,
            ApproveRequired = settings.ApproveRequired,
            KnowledgeEnabled = settings.KnowledgeEnabled,
            VerifyEnabled = settings.VerifyEnabled,
            ProxyEnabled = settings.ProxyEnabled,
            SoftBudgetUsdMicros = settings.SoftBudgetUsdMicros,
            HardBudgetUsdMicros = settings.HardBudgetUsdMicros,
            DomainType = settings.DomainType,
            CustomDomainTypesJson = settings.CustomDomainTypesJson,
            UpdatedAt = settings.UpdatedAt,
            Version = settings.Version,
        };
    }

    public static ProjectSettings ToSettings(ProjectSettingsCacheEntry entry)
    {
        return ProjectSettings.FromSnapshot(
            entry.ProjectId,
            entry.MinIdle,
            entry.MaxConcurrent,
            entry.IdleTtlSeconds,
            entry.ApproveRequired,
            entry.KnowledgeEnabled,
            entry.VerifyEnabled,
            entry.ProxyEnabled,
            entry.SoftBudgetUsdMicros,
            entry.HardBudgetUsdMicros,
            entry.DomainType,
            entry.CustomDomainTypesJson,
            entry.UpdatedAt,
            entry.Version);
    }
}
