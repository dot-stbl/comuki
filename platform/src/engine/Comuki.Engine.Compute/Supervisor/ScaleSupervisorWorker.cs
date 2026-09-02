using Comuki.Engine.Compute.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Compute.Supervisor;

/// <summary>
/// Polling host for <see cref="ScaleSupervisorCycle"/> (issue #3 T2.4): owns
/// the loop and the error policy — a failed pass is logged and retried on the
/// next tick, cancellation exits gracefully. One pass per
/// <see cref="ScaleSupervisorOptions.PollInterval"/>.
/// </summary>
/// <param name="cycle"></param>
/// <param name="scaleOptions"></param>
/// <param name="logger"></param>
public sealed class ScaleSupervisorWorker(
    ScaleSupervisorCycle cycle,
    IOptions<ScaleSupervisorOptions> scaleOptions,
    ILogger<ScaleSupervisorWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Scale supervisor started; poll interval {PollInterval}", scaleOptions.Value.PollInterval);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await cycle.RunAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException)
                {
                    logger.LogError(exception, "Scale supervisor pass failed; retrying next poll");
                }

                try
                {
                    await Task.Delay(scaleOptions.Value.PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            logger.LogInformation("Scale supervisor stopped");
        }
    }
}
