using System.Collections.Concurrent;
using System.Data.Common;
using Comuki.Modules.Projects.Application.Settings;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Projects.Infrastructure.Persistence.Stores;

/// <summary>
/// Keeps the shared settings snapshot cache warm: refreshes every
/// project's row at startup and then on a fixed interval. This is what
/// makes settings live-reload survive a restart — the first pass runs
/// before the supervisor's first poll needs the data, and later passes
/// pick up writes made outside this process.
/// <para>
/// Issue Q27 / v1.1: when the underlying store (DB today, Redis when
/// the planned <c>DistributedProjectSettingsCache</c> lands) is
/// unreachable, the refresher no longer retries and silently waits —
/// it falls back to the last-known snapshot held in
/// <see cref="fallbackSnapshots"/>, each row with a hard
/// <see cref="FallbackTtl"/>. After the TTL elapses the snapshot is
/// dropped so the cache eventually goes cold rather than serving
/// indefinitely-stale data. The metric
/// <c>comuki.projectsettings.cache.fallback_total</c> increments per
/// pass so an operator can see the fallback firing without a log dive.
/// </para>
/// <para>
/// The pass runs as a named system consumer: it owns no subject, and
/// the scope query filters would otherwise confine it to no project's
/// rows.
/// </para>
/// </summary>
/// <param name="dbFactory"></param>
/// <param name="scopeAccessor"></param>
/// <param name="cache"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class ProjectSettingsCacheRefresher(
    IDbContextFactory<ProjectsDbContext> dbFactory,
    ISubjectScopeAccessor scopeAccessor,
    ProjectSettingsCache cache,
    TimeProvider clock,
    ILogger<ProjectSettingsCacheRefresher> logger) : BackgroundService
{
    /// <summary>Poll interval; entries live for <see cref="ProjectSettingsCache.EntryTtl"/> (≈2 passes).</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);

    /// <summary>Hard upper bound on the in-memory fallback snapshot (Q27 / v1.1).</summary>
    public static readonly TimeSpan FallbackTtl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<ProjectId, ProjectSettingsCacheRefresherHelpers.FallbackSnapshotEntry> fallbackSnapshots = new();

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RefreshAllAsync(stoppingToken);
                }
                catch (DbException exception)
                {
                    await ProjectSettingsCacheRefresherHelpers.FallbackAsync(
                        cache,
                        fallbackSnapshots,
                        FallbackTtl,
                        clock.GetUtcNow(),
                        exception,
                        logger,
                        stoppingToken);
                }

                await Task.Delay(RefreshInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // host shutdown (refresh or delay cancelled): the expected stop
            // path — an unhandled cancel here trips StopHost and kills
            // in-flight requests
        }
    }

    /// <summary>Reads every settings row and warms the cache with it.</summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        using var systemScope = scopeAccessor.AsSystem("project-settings-refresher");
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ProjectSettings.AsNoTracking().ToListAsync(cancellationToken);

        var now = clock.GetUtcNow();
        foreach (var settings in rows)
        {
            cache.Warm(settings);
            fallbackSnapshots[settings.ProjectId] = new ProjectSettingsCacheRefresherHelpers.FallbackSnapshotEntry(settings, now);
        }
    }

    /// <summary>
    /// Test-only seam (Q27 / v1.1): seeds the in-memory fallback
    /// dictionary without going through the database. Production code
    /// populates the dictionary via <see cref="RefreshAllAsync"/>;
    /// tests use this method to drive the fallback path with a known
    /// snapshot set, so the fallback behaviour can be exercised
    /// without standing up Postgres. Not intended for production
    /// callers.
    /// </summary>
    /// <param name="settings">Rows to add to the fallback snapshot.</param>
    internal void SeedFallback(IEnumerable<ProjectSettings> settings)
    {
        var now = clock.GetUtcNow();
        foreach (var row in settings)
        {
            cache.Warm(row);
            fallbackSnapshots[row.ProjectId] = new ProjectSettingsCacheRefresherHelpers.FallbackSnapshotEntry(row, now);
        }
    }
}
