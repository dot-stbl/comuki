using Comuki.Host.Translator.Execution.Outcomes;
using Comuki.Host.Translator.Execution.Run;
using Comuki.Host.Translator.Parsing;
using Comuki.Host.Translator.Runtime;

namespace Comuki.Host.Translator.Execution.Loop;

/// <summary>
/// Pumps one pi run: spawns pi on the brief, forwards activities over the
/// worker stream, folds text into the summary, and reduces the whole run
/// to a <see cref="PiOutcome"/> — cancellation (Stop / lease expiry) and
/// pi failures are outcomes, not exceptions.
/// </summary>
public static class PiPump
{
    /// <summary>Runs pi for the claimed brief until it ends, is stopped, or fails.</summary>
    /// <param name="runner"></param>
    /// <param name="run">The run being pumped; <see cref="WorkerRun.RunCancellation"/> is the pump's
    ///     cancellation source — PumpAsync takes no token of its own.</param>
    /// <param name="summary">Fold target: every parsed pi event is observed by it; its result text
    ///     becomes the outcome's on all three exits.</param>
    /// <param name="startedAt">Duration base — the outcome's DurationMs counts elapsed milliseconds
    ///     from this instant to outcome time.</param>
    /// <param name="clock">Read once, at outcome time, to compute DurationMs.</param>
    /// <param name="logger"></param>
    public static async Task<PiOutcome> PumpAsync(
        IPiRunner runner,
        WorkerRun run,
        WorkerRunSummary summary,
        DateTimeOffset startedAt,
        TimeProvider clock,
        ILogger logger)
    {
        try
        {
            await foreach (var line in runner.RunAsync(
                 run.Claimed.Brief,
                 PiEnvironment.FromClaim(run.Claimed),
                 run.RunCancellation.Token))
            {
                foreach (var piEvent in StreamJsonParser.ParseLine(line))
                {
                    summary.Observe(piEvent);
                    if (PiEventToWorkerEvent.ToForwardEvent(run.Claimed.WorkItemId.ToString(), piEvent) is { } forwardable)
                    {
                        await run.Session.SendAsync(forwardable, run.RunCancellation.Token);
                    }
                }
            }

            logger.LogInformation("Pi run of work item {WorkItemId} finished", run.Claimed.WorkItemId);
            return new PiOutcome(
                PiOutcome.SuccessStatus,
                (long)(clock.GetUtcNow() - startedAt).TotalMilliseconds,
                summary.ResultText,
                string.Empty);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Pi run of work item {WorkItemId} cancelled", run.Claimed.WorkItemId);
            return new PiOutcome(
                PiOutcome.CancelledStatus,
                (long)(clock.GetUtcNow() - startedAt).TotalMilliseconds,
                summary.ResultText,
                "run cancelled by orchestrator command or lease expiry");
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "Pi run of work item {WorkItemId} failed", run.Claimed.WorkItemId);
            return new PiOutcome(
                PiOutcome.FailedStatus,
                (long)(clock.GetUtcNow() - startedAt).TotalMilliseconds,
                summary.ResultText,
                exception.Message);
        }
    }
}
