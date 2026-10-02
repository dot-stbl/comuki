using Comuki.Host.Translator.Execution.Run;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Translator.Execution.Commands;

/// <summary>
/// Consumes orchestrator commands for one run: Stop cancels pi (the run
/// reports <c>cancelled</c>), InjectContext appends the context to a file
/// in the working directory, LeaseExpired cancels pi and marks ownership
/// gone so the loop will not complete/fail the item, Exec is the
/// opt-in operator debug surface — accepted only when
/// <c>Translator:DebugExec</c> is true (default off); the refusal is
/// logged but never thrown, the stream keeps consuming. Exec is one-shot:
/// the loop awaits the spawn so that a Stop / LeaseExpired command
/// arriving mid-exec is read on the next channel read instead of being
/// queued behind a long-running operator shell (interactive shells are a
/// separate bidi stream — this surface is for one-shot operator
/// commands).
/// </summary>
/// <param name="run"></param>
/// <param name="workingDirectory"></param>
/// <param name="options"></param>
/// <param name="debugExecHost"></param>
/// <param name="logger"></param>
public sealed class WorkerCommandHandler(
    WorkerRun run,
    string workingDirectory,
    IOptions<TranslatorOptions> options,
    IDebugExecHost debugExecHost,
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
                continue;
            }

            if (command.Exec is { } exec)
            {
                await HandleExecAsync(exec, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Opt-in operator exec dispatch (harden-pi-worker-sandbox 5.3, spec
    /// "Operator debug is opt-in"). The flag check is the gate: off →
    /// log a warning and return; on → refuse malformed (empty command)
    /// with the same warning shape, otherwise hand off to the
    /// <see cref="IDebugExecHost"/> and report the spawn outcome. Either
    /// refusal path is logged at warning, never thrown — the stream
    /// keeps consuming either way, matching the existing LeaseExpired /
    /// Stop refusal shape.
    /// </summary>
    /// <param name="exec">The exec payload the orchestrator delivered.</param>
    /// <param name="stoppingToken">Cancels the spawn when the run shuts down.</param>
    private async Task HandleExecAsync(Shared.Contracts.Grpc.Exec exec, CancellationToken stoppingToken)
    {
        if (!options.Value.DebugExec)
        {
            logger.LogWarning(
                "Refused debug exec on work item {WorkItemId}: Translator:DebugExec is false (default off)",
                run.Claimed.WorkItemId);
            return;
        }

        if (string.IsNullOrWhiteSpace(exec.Command))
        {
            logger.LogWarning(
                "Refused debug exec on work item {WorkItemId}: empty command",
                run.Claimed.WorkItemId);
            return;
        }

        logger.LogInformation(
            "Operator debug exec on work item {WorkItemId}: {Command} (DebugExec=true)",
            run.Claimed.WorkItemId,
            exec.Command);

        var outcome = await debugExecHost.RunAsync(
            new DebugExecRequest(exec.Command, exec.Arguments, workingDirectory),
            stoppingToken);

        if (outcome.IsLaunchFailure)
        {
            logger.LogWarning(
                "Debug exec on work item {WorkItemId} failed to launch: {FailureDetail}",
                run.Claimed.WorkItemId,
                outcome.FailureDetail);
        }
        else
        {
            logger.LogInformation(
                "Debug exec on work item {WorkItemId} exited with {ExitCode}",
                run.Claimed.WorkItemId,
                outcome.ExitCode);
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
