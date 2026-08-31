using System.Text.Json;
using Comuki.Host.Translator.Api;
using Comuki.Host.Translator.Execution;
using Comuki.Host.Translator.Grpc;
using Comuki.Host.Translator.Profiles;
using Comuki.Host.Translator.Runtime;
using Comuki.Shared.Contracts.Grpc;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Translator.Execution;

/// <summary>
/// One claim-execute-report cycle (T3.3): claim an item over REST, prepare
/// profiles, open the worker gRPC stream, run pi under heartbeat + command
/// handling, report the outcome over the stream, then complete/fail the
/// item — unless the lease was lost, in which case ownership is gone and
/// nothing is written. Returns false when the queue had nothing for this
/// worker.
/// </summary>
/// <param name="api"></param>
/// <param name="runner"></param>
/// <param name="workerService"></param>
/// <param name="profilesProvider"></param>
/// <param name="heartbeat"></param>
/// <param name="options"></param>
/// <param name="clock"></param>
/// <param name="loggerFactory"></param>
/// <param name="logger"></param>
public sealed class TranslatorLoop(
    IOrchestratorApi api,
    IPiRunner runner,
    IWorkerService workerService,
    IProfilesProvider profilesProvider,
    HeartbeatMonitor heartbeat,
    IOptions<TranslatorOptions> options,
    TimeProvider clock,
    ILoggerFactory loggerFactory,
    ILogger<TranslatorLoop> logger)
{
    /// <summary>Attempts one full work item cycle. False = queue empty.</summary>
    /// <param name="stoppingToken"></param>
    public async Task<bool> TryRunOnceAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var claim = await api.ClaimAsync(
            new ClaimWorkItemRequest(opts.WorkerImage, opts.ProfilesRef, opts.ProfileKey),
            stoppingToken);
        if (claim.Content is not { } claimed)
        {
            return false;
        }

        logger.LogInformation(
            "Claimed work item {WorkItemId} of run {RunId} (attempt {Attempt})",
            claimed.WorkItemId,
            claimed.RunId,
            claimed.Attempt);

        await profilesProvider.PrepareAsync(opts.ProfilesRef, stoppingToken);

        await using var run = new WorkerRun(
            claimed,
            WorkerSession.Open(workerService, opts.WorkerToken, stoppingToken))
        {
            RunCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken),
        };

        await run.Session.SendAsync(WorkerEventEnvelope.ToStartEvent(claimed), stoppingToken);

        var commandTask = new WorkerCommandHandler(
            run,
            opts.WorkingDirectory,
            loggerFactory.CreateLogger<WorkerCommandHandler>())
            .ConsumeAsync(stoppingToken);
        var heartbeatTask = heartbeat.RunAsync(
            claimed.WorkItemId, opts.HeartbeatInterval, run.RunCancellation.Token, stoppingToken);

        var summary = new WorkerRunSummary();
        var startedAt = clock.GetUtcNow();
        var outcome = await PiPump.PumpAsync(
            runner, run, summary, startedAt, clock, loggerFactory.CreateLogger(nameof(PiPump)));

        try
        {
            await run.Session.SendAsync(
                WorkerEventEnvelope.ToReportEvent(claimed.WorkItemId, outcome),
                CancellationToken.None);
        }
        catch (RpcException exception)
        {
            logger.LogWarning(exception, "Report of work item {WorkItemId} could not be delivered", claimed.WorkItemId);
        }

        run.RunCancellation.Cancel();
        await run.Session.CloseAsync();
        await commandTask;
        var leaseHeld = await heartbeatTask;

        if (leaseHeld is not true || run.LeaseLost)
        {
            logger.LogWarning(
                "Lease of work item {WorkItemId} lost — skipping completion, the reaper owns the item",
                claimed.WorkItemId);
            return true;
        }

        if (outcome.Status == PiOutcome.SuccessStatus)
        {
            var reportJson = JsonSerializer.Serialize(
                WorkerEventEnvelope.ToReportEvent(claimed.WorkItemId, outcome).Report,
                JsonSerializerOptions.Web);
            var completed = await api.CompleteAsync(
                claimed.WorkItemId, new CompleteWorkItemRequest(reportJson), stoppingToken);
            if (!completed.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Complete of work item {WorkItemId} rejected with {StatusCode}",
                    claimed.WorkItemId,
                    completed.StatusCode);
            }

            return true;
        }

        var failureReason = outcome.ErrorText.Length > 0
            ? $"{outcome.Status}: {outcome.ErrorText}"
            : $"status {outcome.Status}";
        var failed = await api.FailAsync(
            claimed.WorkItemId, new FailWorkItemRequest(failureReason), stoppingToken);
        if (!failed.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Fail of work item {WorkItemId} rejected with {StatusCode}",
                claimed.WorkItemId,
                failed.StatusCode);
        }

        return true;
    }
}
