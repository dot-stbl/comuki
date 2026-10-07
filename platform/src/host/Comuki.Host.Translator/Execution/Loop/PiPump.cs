using Comuki.Host.Translator.Api.Contracts;
using Comuki.Host.Translator.Execution.Outcomes;
using Comuki.Host.Translator.Execution.Run;
using Comuki.Host.Translator.Runtime;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Translator.Execution.Loop;

/// <summary>
/// Pumps one harness run: opens the harness session through
/// <see cref="IHarnessRuntime.StartSessionAsync"/>, forwards
/// events over the worker stream, folds text into the summary,
/// and reduces the whole run to a <see cref="PiOutcome"/>.
/// Cancellation (Stop / lease expiry) and harness failures are
/// outcomes, not exceptions.
/// <para>
/// The pump owns the <see cref="IHarnessSession"/> for the
/// duration of the cycle: the <c>await using</c> at the top of
/// the method body guarantees cleanup on success, cancellation,
/// and harness failure alike. <see cref="WorkerRun.HarnessSession"/>
/// is set before the iterator starts so the
/// <c>WorkerCommandHandler</c> can read the session's
/// <see cref="ITurnInputWriter"/> when an inbound
/// <see cref="Shared.Contracts.Grpc.TurnInput"/> arrives
/// mid-cycle.
/// </para>
/// <para>
/// Hardening (harden-worker-runtime Phase 1 + Phase 3, design
/// D1/D2/D4): the pump drives the
/// <see cref="WorkerProgressWatchdog"/> (last-event-age with
/// tier 1/2/3 escalation) and the
/// <see cref="DeadlinePolicy"/> (turn + run wall-clock budgets).
/// On every parsed event, the pump calls
/// <see cref="WorkerProgressWatchdog.Reset"/> — silence past
/// <c>WorkerProgressTimeout</c> ticks the timer. The watchdogs
/// decide the escalation; the pump reads
/// <see cref="WorkerProgressWatchdog.ShouldFailItem"/> /
/// <see cref="DeadlinePolicy.ShouldFailItem"/> on each
/// iteration and short-circuits without forwarding the result.
/// </para>
/// </summary>
public static class PiPump
{
    /// <summary>Runs the harness session for the claimed brief until it ends, is stopped, or fails.</summary>
    /// <param name="api">Orchestrator REST client — used by the watchdogs to call <c>api.FailAsync</c> on tier 3.</param>
    /// <param name="harness">The runtime half of the harness SPI — produces the session, owns the process lifecycle.</param>
    /// <param name="run">The run being pumped; <see cref="WorkerRun.HarnessSession"/> is populated by this method (the command handler reads it later). <see cref="WorkerRun.RunCancellation"/> is the pump's cancellation source.</param>
    /// <param name="summary">Fold target: every parsed harness event is observed by it; its result text becomes the outcome's on all three exits.</param>
    /// <param name="startedAt">Duration base — the outcome's DurationMs counts elapsed milliseconds from this instant to outcome time.</param>
    /// <param name="clock">Read once, at outcome time, to compute DurationMs.</param>
    /// <param name="options">Bound <c>Translator</c> options — the pump reads <c>EventsChannelCapacity</c>, <c>MaxLineLengthBytes</c>, and the timeout / policy values for the watchdogs.</param>
    /// <param name="logger">Top-level pump logger; watchdog / policy loggers are created from the same factory.</param>
    /// <param name="loggerFactory">Used to mint per-component loggers (watchdog, policy, harness).</param>
    public static async Task<PiOutcome> PumpAsync(
        IOrchestratorApi api,
        IHarnessRuntime harness,
        WorkerRun run,
        WorkerRunSummary summary,
        DateTimeOffset startedAt,
        TimeProvider clock,
        IOptions<TranslatorOptions> options,
        ILogger logger,
        ILoggerFactory loggerFactory)
    {
        await using var piEnvironment = await PiExecutionEnvironment.PrepareAsync(
            run.Claimed, Path.GetTempPath(), run.RunCancellation.Token);

        // Backpressure callback — invoked by the bounded events
        // channel when a progress fragment is dropped. The pump
        // journals a worker.events_dropped event over the gRPC
        // stream so the host can increment its
        // events_dropped_total counter (Phase 2 wires the OTel
        // side). The callback is best-effort: a send failure is
        // logged at warning and never propagated to the reader.
        void OnProgressDropped() => _ = TryJournalEventsDroppedAsync(run, loggerFactory);

        var request = new HarnessStartRequest(
            Brief: run.Claimed.Brief,
            Environment: piEnvironment.Environment,
            WorkingDirectory: run.RepositoryDirectory,
            EventsChannelCapacity: options.Value.EventsChannelCapacity,
            MaxLineLengthBytes: options.Value.MaxLineLengthBytes,
            OnProgressDropped: OnProgressDropped);

        await using var session = await harness.StartSessionAsync(
            request, run.RunCancellation.Token);
        run.HarnessSession = session;
        logger.LogInformation(
            "Harness session for work item {WorkItemId} started (process id {ProcessId})",
            run.Claimed.WorkItemId,
            session.ProcessId);

        // Phase 1: arm the watchdogs. The progress watchdog ticks
        // on WorkerProgressTimeout / 6; the deadline policy ticks
        // on min(TurnBudget, RunBudget) / 12. Both own their timers
        // and stop on Dispose. The fail-item path (tier 3 of the
        // watchdog, run-budget breach of the policy) is the pump's
        // existing api.FailAsync call — the watchdogs only cancel
        // the run and set the typed reason, the loop's normal
        // fail path takes over with the right message.
        await using var progressWatchdog = new WorkerProgressWatchdog(
            run, options, clock, loggerFactory.CreateLogger<WorkerProgressWatchdog>());
        await using var deadlinePolicy = new DeadlinePolicy(
            run, options, clock, loggerFactory.CreateLogger<DeadlinePolicy>());
        progressWatchdog.Start();
        deadlinePolicy.Start();

        try
        {
            await foreach (var piEvent in session.Events.WithCancellation(run.RunCancellation.Token))
            {
                // Reset on every parsed event — the watchdogs'
                // shared input is last_event_age, and any event
                // means the harness is making progress.
                progressWatchdog.Reset();
                summary.Observe(piEvent);
                PiRunStateObserver.Observe(run, piEvent);
                if (PiEventToWorkerEvent.ToForwardEvent(run.Claimed.WorkItemId.ToString(), piEvent) is { } forwardable)
                {
                    await run.Session.SendAsync(forwardable, run.RunCancellation.Token);
                }

                // Tier 3 short-circuit: the watchdogs already called
                // api.FailAsync and cancelled the run. The pump
                // exits with cancelled; the loop short-circuits
                // complete because run.StopRequested is true.
                if (progressWatchdog.ShouldFailItem || deadlinePolicy.ShouldFailItem)
                {
                    logger.LogWarning(
                        "Harness run of work item {WorkItemId} aborted: watchdog / deadline policy requested fail-item",
                        run.Claimed.WorkItemId);
                    return new PiOutcome(
                        PiOutcome.FailedStatus,
                        (long)(clock.GetUtcNow() - startedAt).TotalMilliseconds,
                        summary.ResultText,
                        "worker.stall_detected");
                }
            }

            deadlinePolicy.ResetBreachCounter();
            logger.LogInformation("Harness run of work item {WorkItemId} finished", run.Claimed.WorkItemId);
            return new PiOutcome(
                PiOutcome.SuccessStatus,
                (long)(clock.GetUtcNow() - startedAt).TotalMilliseconds,
                summary.ResultText,
                string.Empty);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Harness run of work item {WorkItemId} cancelled", run.Claimed.WorkItemId);
            return new PiOutcome(
                PiOutcome.CancelledStatus,
                (long)(clock.GetUtcNow() - startedAt).TotalMilliseconds,
                summary.ResultText,
                "run cancelled by orchestrator command or lease expiry");
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "Harness run of work item {WorkItemId} failed", run.Claimed.WorkItemId);
            return new PiOutcome(
                PiOutcome.FailedStatus,
                (long)(clock.GetUtcNow() - startedAt).TotalMilliseconds,
                summary.ResultText,
                exception.Message);
        }
        finally
        {
            progressWatchdog.Stop();
            deadlinePolicy.Stop();
        }
    }

    private static async Task TryJournalEventsDroppedAsync(WorkerRun run, ILoggerFactory loggerFactory)
    {
        try
        {
            await run.Session.SendAsync(
                WorkerEventEnvelope.ToEventsDroppedEvent(run.Claimed.WorkItemId, "progress"),
                run.RunCancellation.Token);
        }
        catch (Exception exception)
        {
            loggerFactory
                .CreateLogger("Comuki.Host.Translator.Execution.Loop.PiPump")
                .LogWarning(
                    exception,
                    "Failed to journal events_dropped event for work item {WorkItemId}",
                    run.Claimed.WorkItemId);
        }
    }
}
