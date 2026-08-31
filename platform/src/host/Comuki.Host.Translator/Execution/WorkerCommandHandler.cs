using Comuki.Host.Translator.Grpc;
using Microsoft.Extensions.Logging;

namespace Comuki.Host.Translator.Execution;

/// <summary>
/// Consumes orchestrator commands for one run: Stop cancels pi (the run
/// reports <c>cancelled</c>), InjectContext appends the context to a file
/// in the working directory, LeaseExpired cancels pi and marks ownership
/// gone so the loop will not complete/fail the item.
/// </summary>
/// <param name="run"></param>
/// <param name="workingDirectory"></param>
/// <param name="logger"></param>
public sealed class WorkerCommandHandler(
    WorkerRun run,
    string workingDirectory,
    ILogger<WorkerCommandHandler> logger)
{
    /// <summary>Consumes commands until the session stream ends or the stop token fires.</summary>
    /// <param name="stoppingToken"></param>
    public async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        while (await run.Session.TryReceiveAsync(stoppingToken) is { } command)
        {
            if (command.Stop is { } stop)
            {
                logger.LogWarning("Orchestrator stopped work item {WorkItemId}: {Reason}", run.Claimed.WorkItemId, stop.Reason);
                run.StopRequested = true;
                run.RunCancellation.Cancel();
                continue;
            }

            if (command.InjectContext is { } inject)
            {
                logger.LogInformation("Orchestrator injected context into work item {WorkItemId}", run.Claimed.WorkItemId);
                WorkerContextInjection.Append(workingDirectory, inject.Context);
                continue;
            }

            if (command.LeaseExpired is not null)
            {
                logger.LogWarning("Lease of work item {WorkItemId} expired — ownership is gone", run.Claimed.WorkItemId);
                run.LeaseLost = true;
                run.RunCancellation.Cancel();
            }
        }
    }
}

/// <summary>Appends injected context to <c>comuki-injected-context.md</c> in the working directory.</summary>
internal static class WorkerContextInjection
{
    public const string FileName = "comuki-injected-context.md";

    public static void Append(string workingDirectory, string context)
    {
        File.AppendAllText(
            Path.Combine(workingDirectory, FileName),
            context + Environment.NewLine);
    }
}
