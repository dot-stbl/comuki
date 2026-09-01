using Comuki.Modules.Memory.Application.Ports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// The ephemeral-facts sweeper: deletes facts past their 14-day TTL on a
/// fixed interval (once at startup, then hourly — the TTL is days, an
/// hour of lag is nothing). Sweep failures are logged and retried next
/// interval (the host may start before the database is reachable). This
/// is the only thing in the module that mutates memory on its own —
/// everything else is an explicit write/forget.
/// </summary>
/// <param name="store"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class MemorySweepWorker(
    IMemoryStore store,
    TimeProvider clock,
    ILogger<MemorySweepWorker> logger) : BackgroundService
{
    /// <summary>Sweep interval; the ephemeral TTL is 14 days so an hour of lag is noise.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "memory sweep failed; retrying next interval");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Runs one sweep now; also the test entry point.</summary>
    /// <param name="cancellationToken"></param>
    public async Task SweepOnceAsync(CancellationToken cancellationToken = default)
    {
        var swept = await store.SweepExpiredAsync(clock.GetUtcNow(), cancellationToken);
        if (swept > 0)
        {
            logger.LogInformation("Swept {SweptCount} expired ephemeral memory fact(s)", swept);
        }
    }
}
