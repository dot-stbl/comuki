using Comuki.Host.Translator.Api;
using Comuki.Host.Translator.Parsing;
using Comuki.Host.Translator.Runtime;
using Comuki.Shared.Contracts.Grpc;

namespace Comuki.Host.Translator.Execution;

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
    /// <param name="run"></param>
    /// <param name="summary"></param>
    /// <param name="startedAt"></param>
    /// <param name="clock"></param>
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
            await foreach (var line in runner.RunAsync(run.Claimed.Brief, run.RunCancellation.Token))
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

/// <summary>Start/Report envelope builders over a claim and an outcome.</summary>
public static class WorkerEventEnvelope
{
    /// <summary>The first event of a run: which item, which run, what brief.</summary>
    /// <param name="claimed"></param>
    public static WorkerEvent ToStartEvent(ClaimedWorkItemResponse claimed)
    {
        return new WorkerEvent
        {
            Start = new StageStart
            {
                WorkItemId = claimed.WorkItemId.ToString(),
                RunId = claimed.RunId.ToString(),
                Brief = claimed.Brief,
            },
        };
    }

    /// <summary>The last event of a run: the bottom line.</summary>
    /// <param name="workItemId"></param>
    /// <param name="outcome"></param>
    public static WorkerEvent ToReportEvent(Guid workItemId, PiOutcome outcome)
    {
        return new WorkerEvent
        {
            Report = new StageReport
            {
                WorkItemId = workItemId.ToString(),
                Status = outcome.Status,
                DurationMs = outcome.DurationMs,
                ResultText = outcome.ResultText,
                ErrorText = outcome.ErrorText,
            },
        };
    }
}
