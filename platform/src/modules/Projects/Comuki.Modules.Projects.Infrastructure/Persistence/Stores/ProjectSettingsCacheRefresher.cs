using System.Data.Common;
using Comuki.Modules.Projects.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Projects.Infrastructure.Persistence.Stores;

/// <summary>
/// Keeps the shared settings snapshot cache warm: refreshes every
/// project's row at startup and then on a fixed interval. This is what
/// makes settings live-reload survive a restart — the first pass runs
/// before the supervisor's first poll needs the data, and later passes
/// pick up writes made outside this process. Refresh failures are logged
/// and retried on the next pass (the host may start before the database
/// is reachable); the short cache TTL bounds how long a dead refresher
/// keeps answering.
/// </summary>
/// <param name="dbFactory"></param>
/// <param name="cache"></param>
/// <param name="logger"></param>
public sealed class ProjectSettingsCacheRefresher(
    IDbContextFactory<ProjectsDbContext> dbFactory,
    ProjectSettingsCache cache,
    ILogger<ProjectSettingsCacheRefresher> logger) : BackgroundService
{
    /// <summary>Poll interval; entries live for <see cref="ProjectSettingsCache.EntryTtl"/> (≈2 passes).</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);

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
                    logger.LogWarning(exception, "Project settings refresh failed; retrying next interval");
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
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ProjectSettings.AsNoTracking().ToListAsync(cancellationToken);

        foreach (var settings in rows)
        {
            cache.Warm(settings);
        }
    }
}
