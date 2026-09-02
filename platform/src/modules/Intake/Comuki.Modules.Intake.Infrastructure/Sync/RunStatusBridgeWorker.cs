using Comuki.Modules.Intake.Application.Options;
using Comuki.Modules.Intake.Application.Ports;
using Comuki.Modules.Intake.Application.Sync;
using Comuki.Modules.Intake.Domain.Sync;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Intake.Infrastructure.Sync;

/// <summary>
/// The run status bridge + sync-back outbox drainer (scope-draft §1
/// "Sync"): scans claimed tickets, and for every run that reached a
/// terminal status enqueues one sync job (idempotent on run_id),
/// releases the one-live-run lock, then drains due jobs into the
/// provider transition APIs with exponential backoff. Runs as a hosted
/// service resolving everything through scopes — never captures a
/// scoped dependency.
/// </summary>
/// <param name="scopeFactory"></param>
/// <param name="options"></param>
/// <param name="logger"></param>
public sealed class RunStatusBridgeWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<IntakeOptions> options,
    ILogger<RunStatusBridgeWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BridgeOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // boundary: the worker's own supervision loop — a transient
                // store/provider failure must not kill the hosted service
                logger.LogError(exception, "Intake bridge cycle failed; retrying next interval");
            }

            try
            {
                await Task.Delay(options.Value.BridgeInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task BridgeOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IIntakeStore>();
        var runStatusReader = scope.ServiceProvider.GetRequiredService<IRunStatusReader>();
        var registry = scope.ServiceProvider.GetRequiredService<TicketProviderRegistry>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var now = clock.GetUtcNow();

        await ReleaseFinishedRunsAsync(store, runStatusReader, now, cancellationToken);
        await DrainSyncJobsAsync(store, registry, options.Value, clock, cancellationToken);
    }

    private async Task ReleaseFinishedRunsAsync(
        IIntakeStore store,
        IRunStatusReader runStatusReader,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var claimed = await store.ListClaimedAsync(options.Value.BridgeBatchSize, cancellationToken);
        if (claimed.Count == 0)
        {
            return;
        }

        var statuses = await runStatusReader.ReadStatusesAsync(
            [.. claimed.Select(ticket => ticket.RunId ?? throw new InvalidOperationException($"claimed ticket {ticket.Id} has no run id"))],
            cancellationToken);

        foreach (var ticket in claimed)
        {
            if (!statuses.TryGetValue(ticket.RunId!.Value, out var status)
                || !IntakeRunTerminalStatuses.Terminal.Contains(status))
            {
                continue;
            }

            // sync-back only for tracker-backed tickets; the enqueue is
            // idempotent on run_id (a run is terminal exactly once)
            if (ticket.ConnectionId is { } connectionId)
            {
                var runUrl = IntakeRunUrls.Of(options.Value.PublicBaseUrl, ticket.RunId.Value);
                await store.EnqueueSyncJobAsync(
                    SyncJob.Create(ticket.Id, connectionId, ticket.RunId.Value, ticket.ExternalId, ticket.Url, status, now),
                    cancellationToken);
            }

            await store.ReleaseTicketAsync(ticket.Id, cancellationToken);
            logger.LogInformation(
                "Ticket {TicketId} run {RunId} finished ({Status}) — lock released",
                ticket.Id, ticket.RunId.Value, status);
        }
    }

    private async Task DrainSyncJobsAsync(
        IIntakeStore store,
        TicketProviderRegistry registry,
        IntakeOptions intakeOptions,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var due = await store.ListDueSyncJobsAsync(clock.GetUtcNow(), intakeOptions.BridgeBatchSize, cancellationToken);
        var connections = new Dictionary<Guid, Domain.Connections.SourceConnection>();

        foreach (var job in due)
        {
            var connection = await ResolveConnectionAsync(store, connections, job, cancellationToken);
            if (connection is null)
            {
                await store.MarkSyncJobFailedAsync(job.Id, "connection not found", intakeOptions.SyncMaxAttempts, intakeOptions.SyncBackoff, clock.GetUtcNow(), cancellationToken);
                continue;
            }

            var syncPort = registry.FindSync(TicketProviderKeys.Key(connection.Provider));
            if (syncPort is null)
            {
                await store.MarkSyncJobFailedAsync(job.Id, "no sync port registered for provider", intakeOptions.SyncMaxAttempts, intakeOptions.SyncBackoff, clock.GetUtcNow(), cancellationToken);
                continue;
            }

            try
            {
                await syncPort.TransitionAsync(
                    connection,
                    new TicketTransition(job.ExternalId, job.ExternalUrl, job.RunStatus, IntakeRunUrls.Of(intakeOptions.PublicBaseUrl, job.RunId)),
                    cancellationToken);
                await store.MarkSyncJobDoneAsync(job.Id, clock.GetUtcNow(), cancellationToken);
                logger.LogInformation("Sync job {JobId} pushed {RunStatus} for {ExternalId}", job.Id, job.RunStatus, job.ExternalId);
            }
            catch (Exception exception)
            {
                // boundary: per-job isolation — one failing tracker call
                // schedules its retry and never stops the drain loop
                logger.LogWarning(exception, "Sync job {JobId} attempt {Attempt} failed", job.Id, job.Attempts + 1);
                await store.MarkSyncJobFailedAsync(job.Id, exception.Message, intakeOptions.SyncMaxAttempts, intakeOptions.SyncBackoff, clock.GetUtcNow(), cancellationToken);
            }
        }
    }

    private static async Task<Domain.Connections.SourceConnection?> ResolveConnectionAsync(
        IIntakeStore store,
        Dictionary<Guid, Domain.Connections.SourceConnection> connections,
        SyncJob job,
        CancellationToken cancellationToken)
    {
        if (connections.TryGetValue(job.ConnectionId.Value, out var cached))
        {
            return cached;
        }

        var connection = await store.FindConnectionAsync(job.ConnectionId, cancellationToken);
        if (connection is { })
        {
            connections[connection.Id.Value] = connection;
        }

        return connection;
    }
}

/// <summary>Terminal run statuses the bridge reacts to (PascalCase enum names).</summary>
file static class IntakeRunTerminalStatuses
{
    public static readonly IReadOnlySet<string> Terminal =
        new HashSet<string>(["Succeeded", "Failed", "Cancelled"], StringComparer.Ordinal);
}

/// <summary>Run URL composition for the sync comments.</summary>
file static class IntakeRunUrls
{
    public static Uri Of(Uri publicBaseUrl, RunId runId)
    {
        return new Uri(publicBaseUrl, $"runs/{runId.Value}");
    }
}
