using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Artifacts.Application.Packaging;

/// <summary>
/// Polling driver for <see cref="RunArtifactPackager"/>. Wakes up on a
/// fixed interval, asks the run source for terminal-but-not-bundled runs
/// and bundles each one. Errors on one run do not stop the loop — the
/// exception is logged and the driver moves on, so a transient MinIO
/// outage does not stall the queue.
/// </summary>
/// <param name="packager">Per-run bundler.</param>
/// <param name="runSource">Read-side over the orchestration schema (host-composed).</param>
/// <param name="logger">Structured logger.</param>
public sealed class RunArtifactPackagerService(
    RunArtifactPackager packager,
    IRunArtifactRunSource runSource,
    ILogger<RunArtifactPackagerService> logger) : BackgroundService
{
    /// <summary>Default poll interval — overridable by tests via the constructor.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(10);

    /// <summary>Default per-poll cap on candidates processed.</summary>
    public const int DefaultBatchLimit = 50;

    private static readonly TimeSpan pollInterval = DefaultPollInterval;
    private const int BatchLimit = DefaultBatchLimit;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Run artifact packager started (interval {Interval}s, batch {Batch})",
            pollInterval.TotalSeconds,
            BatchLimit);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Run artifact packager poll failed; will retry after {Interval}s",
                    pollInterval.TotalSeconds);
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Run artifact packager stopped");
    }

    /// <summary>
    /// Runs one polling cycle — exposed for integration tests that need to
    /// drive the packager deterministically. Returns the number of runs
    /// bundled in this cycle.
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task<int> PollOnceAsync(CancellationToken cancellationToken)
    {
        var bundled = 0;
        await foreach (var candidate in runSource.ListUnbundledTerminalAsync(BatchLimit, cancellationToken))
        {
            var outcome = await packager.BundleAsync(candidate, cancellationToken);
            if (outcome is not null)
            {
                bundled++;
            }
        }

        if (bundled > 0)
        {
            logger.LogInformation("Run artifact packager bundled {Count} run(s) in this cycle", bundled);
        }

        return bundled;
    }
}
