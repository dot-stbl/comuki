using Comuki.Host.Translator.Api.Contracts;
using Comuki.Host.Translator.Api.Models.Requests;
using Refit;

namespace Comuki.Host.Translator.Execution.Loop;

/// <summary>
/// Extends the lease of a running item on a fixed cadence until the run
/// ends. A rejected heartbeat (409 — the reaper took the item) flips the
/// run to lease-lost: pi is cancelled and completion is skipped. An
/// upstream heartbeat failure (network drop, Polly timeout, orchestrator
/// 5xx) is treated the same way — a heartbeat exception means we cannot
/// prove the lease is still held, so the loop bails out as if the
/// orchestrator had rejected the heartbeat, and the TranslatorLoop's
/// existing "lease-lost skips completion" path takes over without
/// killing the host process.
/// </summary>
/// <param name="api"></param>
/// <param name="logger">Records the thrown exception so the operator can investigate, but does not let it propagate — see <see cref="RunAsync"/>'s catch contract.</param>
public sealed class HeartbeatMonitor(IOrchestratorApi api, ILogger<HeartbeatMonitor> logger)
{
    /// <summary>
    /// Heartbeats until cancelled. Returns true when the run ended with
    /// the lease held, false when the orchestrator rejected a heartbeat
    /// (409 — the reaper took the item) OR when the heartbeat call threw
    /// (network drop, Polly timeout, orchestrator 5xx). The two paths
    /// share the same semantics: ownership is no longer certain, so
    /// completion must be skipped and the reaper owns the item.
    /// </summary>
    /// <param name="workItemId"></param>
    /// <param name="generation">The generation the run claimed this item under — echo on every heartbeat so the host can reject a stale generation as an ownership miss.</param>
    /// <param name="interval"></param>
    /// <param name="runToken"></param>
    /// <param name="stoppingToken"></param>
    public async Task<bool> RunAsync(Guid workItemId, int generation, TimeSpan interval, CancellationToken runToken, CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(runToken, stoppingToken);
        while (!linked.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, linked.Token);
            }
            catch (OperationCanceledException)
            {
                return true;
            }

            IApiResponse response;
            try
            {
                response = await api.HeartbeatAsync(workItemId, new HeartbeatWorkItemRequest(generation), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Run-token tripped during the in-flight heartbeat call —
                // the run is shutting down on its own, treat as a clean exit.
                return true;
            }
            catch (Exception exception)
            {
                // Upstream failure (Polly timeout, HttpRequestException,
                // orchestrator 5xx, etc.) is the same shape as a rejected
                // heartbeat for lease purposes: we cannot prove the
                // lease is still held, so we bail as if the orchestrator
                // had rejected it. The exception is logged so the
                // operator can investigate the network/restart; the loop
                // sees `false` and skips completion without killing the
                // host.
                logger.LogError(
                    exception,
                    "Heartbeat on work item {WorkItemId} (generation {Generation}) threw — treating as lease-lost",
                    workItemId,
                    generation);
                return false;
            }

            if (response.IsSuccessStatusCode)
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
