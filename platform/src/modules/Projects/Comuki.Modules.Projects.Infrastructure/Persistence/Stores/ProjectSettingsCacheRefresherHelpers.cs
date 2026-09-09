using System.Collections.Concurrent;
using System.Data.Common;
using Comuki.Modules.Projects.Application.Settings;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Telemetry;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Projects.Infrastructure.Persistence.Stores;

/// <summary>
/// Pure-logic helpers for <see cref="ProjectSettingsCacheRefresher"/>.
/// Extracted from the refresher so the class holds only orchestration
/// (per <c>class-layout-and-tooling.md §1a</c>). Each helper is a
/// function over its inputs — no instance state is captured.
/// </summary>
internal static class ProjectSettingsCacheRefresherHelpers
{
    /// <summary>
    /// Re-warms the cache from the last-known good snapshot when the
    /// underlying store is unreachable (Q27 / v1.1). Snapshots older
    /// than <paramref name="fallbackTtl"/> are dropped — the cache goes
    /// cold and reads start returning <c>null</c> rather than serving
    /// indefinitely-stale data.
    /// </summary>
    /// <param name="cache">Live settings cache to re-warm.</param>
    /// <param name="fallbackSnapshots">Last-known good snapshots per project.</param>
    /// <param name="fallbackTtl">Hard upper bound on the in-memory fallback snapshot.</param>
    /// <param name="now">Wall-clock anchor (injected <see cref="TimeProvider"/>).</param>
    /// <param name="exception">The DB exception that triggered the fallback.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task FallbackAsync(
        ProjectSettingsCache cache,
        ConcurrentDictionary<ProjectId, FallbackSnapshotEntry> fallbackSnapshots,
        TimeSpan fallbackTtl,
        DateTimeOffset now,
        DbException exception,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var warmed = 0;
        var expired = 0;

        foreach (var entry in fallbackSnapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var age = now - entry.Value.CapturedAt;
            if (age > fallbackTtl)
            {
                fallbackSnapshots.TryRemove(entry.Key, out _);
                expired++;
                continue;
            }

            cache.Warm(entry.Value.Settings);
            warmed++;
        }

        ComukiTelemetry.ProjectSettingsCacheFallback.Add(1);

        logger.LogWarning(
            exception,
            "Project settings refresh failed; served {WarmedCount} snapshot(s) from in-memory fallback ({ExpiredCount} expired, ttl {TtlSeconds}s)",
            warmed,
            expired,
            (int)fallbackTtl.TotalSeconds);

        return Task.CompletedTask;
    }

    /// <summary>
    /// One captured snapshot and the wall-clock stamp it was captured at.
    /// Backing storage for the fallback snapshots; the stamp is what
    /// bounds the fallback TTL.
    /// </summary>
    /// <param name="Settings"></param>
    /// <param name="CapturedAt"></param>
    internal sealed record FallbackSnapshotEntry(ProjectSettings Settings, DateTimeOffset CapturedAt);
}
