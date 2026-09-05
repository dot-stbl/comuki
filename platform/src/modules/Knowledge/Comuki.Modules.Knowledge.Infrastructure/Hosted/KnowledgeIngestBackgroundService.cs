using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Knowledge.Infrastructure.Hosted;

/// <summary>
/// Periodic doc worker — polls the corpus (v0: no KnowledgeSource table
/// yet, so the loop is a heartbeat that logs readiness + checks the
/// pgvector availability). When the KnowledgeSource table lands in a
/// later slice, this loop becomes the dispatcher: every pending source
/// row triggers a per-document <see cref="IKnowledgeIngestor"/> call.
/// </summary>
public sealed class KnowledgeIngestBackgroundService(
    IServiceProvider services,
    IOptions<KnowledgeIngestOptions> options,
    ILogger<KnowledgeIngestBackgroundService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The `services` parameter is reserved for the future dispatcher:
        // every pending KnowledgeSource row will resolve a fresh
        // IKnowledgeIngestor scope from it. v0 is a heartbeat.
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));
        logger.LogInformation("knowledge doc worker started (interval {IntervalSeconds}s)", interval.TotalSeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "knowledge doc worker tick failed; continuing");
                }

                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown — fall through.
        }
        finally
        {
            logger.LogInformation("knowledge doc worker stopped");
        }
    }

    private Task TickAsync(CancellationToken cancellationToken)
    {
        // The cancellation token is reserved for the future sweep: a row-by-row
        // IKnowledgeIngestor scope will honor shutdown between sources. v0 is a
        // heartbeat — the surrounding ExecuteAsync loop already breaks on
        // OperationCanceledException, so the token would be redundant here.
        logger.LogDebug("knowledge doc worker heartbeat (no sources yet)");
        return Task.CompletedTask;
    }
}
