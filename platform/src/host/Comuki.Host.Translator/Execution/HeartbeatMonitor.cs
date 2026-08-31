using Comuki.Host.Translator.Api;

namespace Comuki.Host.Translator.Execution;

/// <summary>
/// Extends the lease of a running item on a fixed cadence until the run
/// ends. A rejected heartbeat (409 — the reaper took the item) flips the
/// run to lease-lost: pi is cancelled and completion is skipped.
/// </summary>
/// <param name="api"></param>
public sealed class HeartbeatMonitor(IOrchestratorApi api)
{
    /// <summary>
    /// Heartbeats until cancelled. Returns true when the run ended with the
    /// lease held, false when the orchestrator rejected a heartbeat.
    /// </summary>
    /// <param name="workItemId"></param>
    /// <param name="interval"></param>
    /// <param name="runToken"></param>
    /// <param name="stoppingToken"></param>
    public async Task<bool> RunAsync(Guid workItemId, TimeSpan interval, CancellationToken runToken, CancellationToken stoppingToken)
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

            var response = await api.HeartbeatAsync(workItemId, stoppingToken);
            if (response.IsSuccessStatusCode)
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
