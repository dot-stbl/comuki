using Comuki.Host.Translator.Execution.Run;
using Comuki.Host.Translator.Parsing;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Translator.Execution.Commands;

/// <summary>
/// Consumes orchestrator commands for one run: Stop cancels the
/// harness (the run reports <c>cancelled</c>), InjectContext
/// appends the context to a file in the working directory,
/// LeaseExpired cancels the harness and marks ownership gone so
/// the loop will not complete/fail the item, Exec is the opt-in
/// operator debug surface — accepted only when
/// <c>Translator:DebugExec</c> is true (default off); the refusal
/// is logged but never thrown, the stream keeps consuming. Exec
/// is one-shot: the loop awaits the spawn so that a Stop /
/// LeaseExpired command arriving mid-exec is read on the next
/// channel read instead of being queued behind a long-running
/// operator shell (interactive shells are a separate bidi stream
/// — this surface is for one-shot operator commands).
/// <para>
/// TurnInput is the live-session baton (add-orchestra Phase 1c —
/// <c>specs/session/spec.md</c> Requirement "TurnInput is the
/// authoritative session turn"). The handler reads the
/// <c>WorkerRun.HarnessSession</c> the <c>PiPump</c> populated
/// and writes the operator's text into the harness's stdin
/// writer: <c>steer</c> mid-flight, <c>follow_up</c> after
/// <c>agent_settled</c>. The harness protocol
/// (<c>openspec/changes/add-orchestra/spike-1b-report.md</c>) is
/// the documented JSON-RPC-over-stdio channel; the per-command
/// choice is informed by the last seen
/// <see cref="PiEvent.AgentSettledEvent"/> on the events stream.
/// </para>
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

            if (command.TurnInput is { } turn)
            {
                HandleTurnInput(turn);
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

    /// <summary>
    /// Live-session baton (add-orchestra Phase 1c) — the worker
    /// forwards the operator's text into the harness's stdin as a
    /// <c>steer</c> or <c>follow_up</c> JSON-RPC command
    /// (<c>openspec/changes/add-orchestra/spike-1b-report.md</c>).
    /// The choice between the two depends on whether the agent
    /// has settled: <c>steer</c> is the mid-flight delivery,
    /// <c>follow_up</c> queues the message for the next turn.
    /// <para>
    /// The <see cref="Shared.Kernel.Harness.HarnessCapabilities"/>
    /// policy gate (LiveSession = true) is the orchestrator-side
    /// check the steering endpoint runs; the worker accepts the
    /// command regardless because the wire shape is symmetric —
    /// a turn sent on a no-LiveSession harness degrades into the
    /// follow-up WorkItem path on the orchestrator side, not here.
    /// </para>
    /// <para>
    /// Failure shape: empty text is dropped with a warning
    /// (the same refusal as <see cref="HandleExecAsync"/>'s
    /// empty <c>Command</c>); a closed harness stream returns
    /// <c>false</c> from the writer and the worker logs a warning
    /// and returns — the stream keeps consuming. The
    /// <see cref="WorkerRun.RunCancellation"/> is the same token
    /// the pump honours; the worker doesn't take an extra token
    /// of its own.
    /// </para>
    /// </summary>
    /// <param name="turn">The session turn the orchestrator delivered.</param>
    private void HandleTurnInput(Shared.Contracts.Grpc.TurnInput turn)
    {
        if (string.IsNullOrWhiteSpace(turn.Text))
        {
            logger.LogWarning(
                "Refused turn input on work item {WorkItemId}: empty text",
                run.Claimed.WorkItemId);
            return;
        }

        var session = run.HarnessSession;
        if (session is null)
        {
            // The pump hasn't populated the session yet (the turn
            // arrived before the harness process came up) or the
            // session is gone. The bidi stream accepted the
            // command, the harness's writer is unavailable — the
            // operator's steer is dropped. Same refusal shape
            // the orchestrator uses for the no-LiveSession
            // follow-up branch (delivered:false).
            logger.LogWarning(
                "Turn input on work item {WorkItemId} dropped: harness session is not available (delivered:false)",
                run.Claimed.WorkItemId);
            return;
        }

        var turnId = $"s-{Guid.NewGuid():N}";
        var settled = run.HasAgentSettled;
        var written = settled
            ? session.TurnInputs.TryWriteFollowUp(turnId, turn.Text)
            : session.TurnInputs.TryWriteSteer(turnId, turn.Text);

        if (!written)
        {
            logger.LogWarning(
                "Turn input on work item {WorkItemId} dropped: harness stdin closed (delivered:false, settled={Settled})",
                run.Claimed.WorkItemId,
                settled);
            return;
        }

        logger.LogInformation(
            "Operator turn input on work item {WorkItemId} forwarded to harness (role={Role}, text-length={TextLength}, settled={Settled})",
            run.Claimed.WorkItemId,
            turn.Role,
            turn.Text.Length,
            settled);
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
