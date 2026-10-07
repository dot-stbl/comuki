using Comuki.Host.Translator.Execution.Outcomes;
using Comuki.Host.Translator.Execution.Run;
using Comuki.Host.Translator.Runtime;

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
/// </summary>
public static class PiPump
{
    /// <summary>Runs the harness session for the claimed brief until it ends, is stopped, or fails.</summary>
    /// <param name="harness">The runtime half of the harness SPI — produces the session, owns the process lifecycle.</param>
    /// <param name="run">The run being pumped; <see cref="WorkerRun.HarnessSession"/> is populated by this method (the command handler reads it later). <see cref="WorkerRun.RunCancellation"/> is the pump's cancellation source.</param>
    /// <param name="summary">Fold target: every parsed harness event is observed by it; its result text becomes the outcome's on all three exits.</param>
    /// <param name="startedAt">Duration base — the outcome's DurationMs counts elapsed milliseconds from this instant to outcome time.</param>
    /// <param name="clock">Read once, at outcome time, to compute DurationMs.</param>
    /// <param name="logger"></param>
    public static async Task<PiOutcome> PumpAsync(
        IHarnessRuntime harness,
        WorkerRun run,
        WorkerRunSummary summary,
        DateTimeOffset startedAt,
        TimeProvider clock,
        ILogger logger)
    {
        await using var piEnvironment = await PiExecutionEnvironment.PrepareAsync(
            run.Claimed, Path.GetTempPath(), run.RunCancellation.Token);

        var request = new HarnessStartRequest(
            Brief: run.Claimed.Brief,
            Environment: piEnvironment.Environment,
            WorkingDirectory: run.RepositoryDirectory);

        // Disposal is hand-rolled (not `await using var session`) so the
        // post-disposal step below can read `session.ExitCode` — the
        // `using var` form disposes at method exit, *after* we've already
        // returned. The harness session's `DisposeAsync` waits for the
        // process to exit and captures `process.ExitCode` there, so
        // `session.ExitCode` is only valid *after* this `try/finally`.
        var session = await harness.StartSessionAsync(
            request, run.RunCancellation.Token);
        run.HarnessSession = session;
        logger.LogInformation(
            "Harness session for work item {WorkItemId} started (process id {ProcessId})",
            run.Claimed.WorkItemId,
            session.ProcessId);

        PiOutcome outcome;
        try
        {
            await foreach (var piEvent in session.Events.WithCancellation(run.RunCancellation.Token))
            {
                summary.Observe(piEvent);
                PiRunStateObserver.Observe(run, piEvent);
                if (PiEventToWorkerEvent.ToForwardEvent(run.Claimed.WorkItemId.ToString(), piEvent) is { } forwardable)
                {
                    await run.Session.SendAsync(forwardable, run.RunCancellation.Token);
                }
            }

            logger.LogInformation("Harness run of work item {WorkItemId} finished", run.Claimed.WorkItemId);
            outcome = new PiOutcome(
                PiOutcome.SuccessStatus,
                (long)(clock.GetUtcNow() - startedAt).TotalMilliseconds,
                summary.ResultText,
                string.Empty);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Harness run of work item {WorkItemId} cancelled", run.Claimed.WorkItemId);
            outcome = new PiOutcome(
                PiOutcome.CancelledStatus,
                (long)(clock.GetUtcNow() - startedAt).TotalMilliseconds,
                summary.ResultText,
                "run cancelled by orchestrator command or lease expiry");
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "Harness run of work item {WorkItemId} failed", run.Claimed.WorkItemId);
            outcome = new PiOutcome(
                PiOutcome.FailedStatus,
                (long)(clock.GetUtcNow() - startedAt).TotalMilliseconds,
                summary.ResultText,
                exception.Message);
        }
        finally
        {
            await session.DisposeAsync();
        }

        // Exit-code harness check (worker-runtime spec scenario "Non-zero
        // pi exit fails the item"). The events iterator completes on
        // stdout EOF; the process then either exits 0 (orderly shutdown
        // via stdin close — pi's documented path) or crashes. A
        // non-zero ExitCode on an otherwise-Success outcome means the
        // run streamed its events but the harness died mid-run —
        // fail the item rather than report success. Cancellation/InvalidOp
        // outcomes already carry their own failure shape and pass through.
        if (outcome.Status == PiOutcome.SuccessStatus
            && session.ExitCode is int exitCode
            && exitCode != 0)
        {
            // StderrTail is the operator-facing slice of the harness's
            // captured stderr (production: bounded tail, the production
            // PiRpcSession trims to a 4 KiB cap; the in-process fake
            // returns null). Append it to the ErrorText so the operator
            // sees the failing harness's last lines alongside the exit
            // code, per the spec scenario "outcome is failed carrying
            // the exit code and stderr".
            var errorText = session.StderrTail is { Length: > 0 } stderrTail
                ? $"harness exited with code {exitCode}\nstderr:\n{stderrTail}"
                : $"harness exited with code {exitCode}";
            logger.LogError(
                "Harness run of work item {WorkItemId} finished cleanly but exited with code {ExitCode} — failing the item",
                run.Claimed.WorkItemId,
                exitCode);
            return new PiOutcome(
                PiOutcome.FailedStatus,
                outcome.DurationMs,
                outcome.ResultText,
                errorText);
        }

        return outcome;
    }
}
