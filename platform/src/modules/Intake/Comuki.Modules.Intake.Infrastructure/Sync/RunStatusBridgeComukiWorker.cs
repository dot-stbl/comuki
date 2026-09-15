using System.Data.Common;
using System.Text.Json;
using Comuki.Modules.Intake.Application.Options;
using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Application.Sync;
using Comuki.Modules.Intake.Domain.Sync;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Intake.Infrastructure.Sync;

/// <summary>
/// The run status bridge + sync-back outbox drainer behind the comuki worker
/// registry (scope-draft §1 "Sync"): scans claimed tickets, and for every run
/// that reached a terminal status enqueues one sync job (idempotent on
/// run_id), releases the one-live-run lock, then drains due jobs into the
/// provider transition APIs with exponential backoff. Each cycle runs
/// AsSystem: the bridge reads runs across every project, ambient subject
/// scope does not apply. A transient cycle failure is reported to the
/// registry, which retries with backoff.
/// </summary>
/// <param name="scopeAccessor">Establishes the AsSystem subject scope for each cycle.</param>
/// <param name="options">Bound intake options; the bridge interval drives the schedule.</param>
/// <param name="logger">Structured logger — per-ticket and per-job Information.</param>
public sealed class RunStatusBridgeComukiWorker(
    ISubjectScopeAccessor scopeAccessor,
    IOptions<IntakeOptions> options,
    ILogger<RunStatusBridgeComukiWorker> logger) : IComukiWorker
{
    /// <inheritdoc />
    public string Name => "intake-bridge";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Interval(options.Value.BridgeInterval);

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var systemScope = scopeAccessor.AsSystem(Name);

            var outcome = await RunStatusBridgePass.RunAsync(
                context.Services,
                context.Clock,
                options.Value,
                logger,
                cancellationToken);

            return WorkerResult.Ok($"released {outcome.Released}, drained {outcome.Drained}", outcome);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException
                                   or DbException or JsonException)
        {
            // boundary: the worker's own supervision loop — a transient
            // store/provider failure counts as a failed cycle for the
            // registry (logged, backoff); it must not kill the host
            return WorkerResult.Fail($"bridge cycle failed: {exception.Message}");
        }
    }
}

/// <summary>One bridge cycle outcome — released locks and drained sync jobs.</summary>
/// <param name="Released">Tickets whose terminal run released the live-run lock.</param>
/// <param name="Drained">Sync jobs pushed to their provider this cycle.</param>
file sealed record BridgePassOutcome(int Released, int Drained);

/// <summary>
/// File-scoped pass logic for the bridge worker: one cycle is one release
/// sweep over claimed tickets followed by one drain of due sync jobs, both
/// resolved from the registry's per-cycle scope. A single static class is
/// the smallest unit that keeps the worker free of private methods (rule
/// code-shape §9 / class-layout-and-tooling §1a).
/// </summary>
file static class RunStatusBridgePass
{
    public static async Task<BridgePassOutcome> RunAsync(
        IServiceProvider services,
        TimeProvider clock,
        IntakeOptions intakeOptions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<IIntakeStore>();
        var runStatusReader = services.GetRequiredService<IRunStatusReader>();
        var registry = services.GetRequiredService<TicketProviderRegistry>();
        var now = clock.GetUtcNow();

        var released = await ReleaseFinishedRunsAsync(store, runStatusReader, intakeOptions, now, logger, cancellationToken);
        var drained = await DrainSyncJobsAsync(store, registry, intakeOptions, clock, logger, cancellationToken);

        return new BridgePassOutcome(released, drained);
    }

    public static async Task<int> ReleaseFinishedRunsAsync(
        IIntakeStore store,
        IRunStatusReader runStatusReader,
        IntakeOptions intakeOptions,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var claimed = await store.ListClaimedAsync(intakeOptions.BridgeBatchSize, cancellationToken);
        if (claimed.Count == 0)
        {
            return 0;
        }

        var statuses = await runStatusReader.ReadStatusesAsync(
            [.. claimed.Select(static ticket => ticket.RunId ?? throw new InvalidOperationException($"claimed ticket {ticket.Id} has no run id"))],
            cancellationToken);

        var released = 0;
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
                var runUrl = IntakeRunUrls.Of(intakeOptions.PublicBaseUrl, ticket.RunId.Value);
                await store.EnqueueSyncJobAsync(
                    SyncJob.Create(ticket.Id, connectionId, ticket.RunId.Value, ticket.ExternalId, ticket.Url, status, now),
                    cancellationToken);
            }

            await store.ReleaseTicketAsync(ticket.Id, cancellationToken);
            logger.LogInformation(
                "Ticket {TicketId} run {RunId} finished ({Status}) — lock released",
                ticket.Id, ticket.RunId.Value, status);
            released++;
        }

        return released;
    }

    public static async Task<int> DrainSyncJobsAsync(
        IIntakeStore store,
        TicketProviderRegistry registry,
        IntakeOptions intakeOptions,
        TimeProvider clock,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var due = await store.ListDueSyncJobsAsync(clock.GetUtcNow(), intakeOptions.BridgeBatchSize, cancellationToken);
        var connections = new Dictionary<Guid, Domain.Connections.SourceConnection>();
        var drained = 0;

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
                // the job carries the ticket id but not the kind; load the
                // ticket here so the sync port can decide close-on-success
                // (issues) vs. comment-only (PRs).
                var kind = await store.FindTicketAsync(job.TicketId, cancellationToken) is { } ticket
                    ? ticket.Kind
                    : InboundTicketKind.Issue;

                await syncPort.TransitionAsync(
                    connection,
                    new TicketTransition(job.ExternalId, job.ExternalUrl, job.RunStatus, IntakeRunUrls.Of(intakeOptions.PublicBaseUrl, job.RunId), kind),
                    cancellationToken);
                await store.MarkSyncJobDoneAsync(job.Id, clock.GetUtcNow(), cancellationToken);
                logger.LogInformation("Sync job {JobId} pushed {RunStatus} for {ExternalId}", job.Id, job.RunStatus, job.ExternalId);
                drained++;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException or JsonException
                                       or DbException)
            {
                // boundary: per-job isolation — one failing tracker call
                // (or its follow-up store write) schedules a retry and never
                // stops the drain loop
                logger.LogWarning(exception, "Sync job {JobId} attempt {Attempt} failed", job.Id, job.Attempts + 1);
                await store.MarkSyncJobFailedAsync(job.Id, exception.Message, intakeOptions.SyncMaxAttempts, intakeOptions.SyncBackoff, clock.GetUtcNow(), cancellationToken);
            }
        }

        return drained;
    }

    public static async Task<Domain.Connections.SourceConnection?> ResolveConnectionAsync(
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

/// <summary>Terminal run statuses the bridge reacts to (<see cref="RunStatuses"/> keys).</summary>
file static class IntakeRunTerminalStatuses
{
    public static readonly IReadOnlySet<string> Terminal =
        new HashSet<string>([RunStatuses.Succeeded, RunStatuses.Failed, RunStatuses.Cancelled], StringComparer.Ordinal);
}

/// <summary>Run URL composition for the sync comments.</summary>
file static class IntakeRunUrls
{
    public static Uri Of(Uri publicBaseUrl, RunId runId)
    {
        return new Uri(publicBaseUrl, $"runs/{runId.Value}");
    }
}
