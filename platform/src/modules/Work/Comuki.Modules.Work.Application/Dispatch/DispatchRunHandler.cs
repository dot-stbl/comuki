using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Inbox;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Shared.Contracts;
using Comuki.Shared.Contracts.Work;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Application.Dispatch;

/// <summary>
/// <c>Work.DispatchRun</c> command — emits the
/// <c>Orchestration.StartRun</c> command into the Work outbox in
/// the same transaction as the WorkTask state change
/// (<c>Ready</c> / <c>Blocked</c> → <c>Active</c>). The one-active-Run
/// invariant is enforced at the aggregate guard: a second
/// concurrent dispatch against the same Task with a fresh run
/// id is rejected with
/// <see cref="WorkTaskErrorCodes.RunAlreadyActive"/>; with the
/// SAME run id it is a no-op (the dispatch inbox claim mirrors
/// the WS9 admission-claim pattern). The published event is
/// <c>work.task.attempt-requested.v1</c>; the engine-side
/// <c>WorkDispatchRequestedSubscriber</c> reads the outbox row,
/// calls the host-composed <c>IRunLauncher.LaunchAsync</c>
/// (Work-dispatch overload, task 2.3), and the engine creates
/// the Run. The Task is in <c>Active</c> before the Run is even
/// created — the engine back-fills the run id on the Work
/// outbox row only after the dispatch has won; the Task's
/// <see cref="Domain.WorkTask.AppendAttempt"/> transition is
/// the local mirror.
/// </summary>
public sealed record DispatchRunCommand(
    WorkTaskId TaskId,
    RunId RunId,
    WorkDispatchItem Dispatch);

/// <summary>Outcome of a dispatch call.</summary>
public sealed record DispatchRunOutcome(int AttemptOrdinal, bool WasReplay);

/// <summary>
/// Handler for <see cref="DispatchRunCommand"/>. The
/// <see cref="IWorkInbox"/> claim is the dedupe ledger — a
/// retried dispatch with the same run id returns
/// <see cref="DispatchRunOutcome.WasReplay"/> = true and the
/// unchanged attempt ordinal. A second concurrent dispatch with a
/// fresh run id is rejected with
/// <see cref="WorkTaskErrorCodes.RunAlreadyActive"/>.
/// </summary>
public sealed class DispatchRunHandler(
    IWorkTaskStore store,
    IOutbox outbox,
    TimeProvider clock,
    ILogger<DispatchRunHandler> logger)
{
    /// <summary>Handles the command. Idempotent on the supplied <paramref name="command.RunId"/>.</summary>
    /// <exception cref="WorkTaskDomainException">The one-active-Run invariant or the status guard rejects the dispatch.</exception>
    public async Task<DispatchRunOutcome> HandleAsync(DispatchRunCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Dispatch is null)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.DispatchPayloadMissing,
                $"dispatch payload must not be null on Task {command.TaskId}");
        }

        var task = await store.FindAsync(command.TaskId, cancellationToken)
            ?? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TaskNotFound,
                $"task {command.TaskId} not found");

        // The aggregate's AppendAttempt is the one-active-Run
        // guard. The idempotency seam (same run id re-launch)
        // lives in the aggregate too — it returns the same
        // ordinal and skips the bump on a no-op replay. We
        // detect a replay by snapshotting the ordinal BEFORE
        // the call and comparing AFTER; the aggregate returns
        // the same ordinal with no state change, so equality
        // means replay.
        var ordinalBefore = task.AttemptOrdinal;
        var ordinal = task.AppendAttempt(command.RunId, clock.GetUtcNow());
        var wasReplay = ordinal == ordinalBefore;

        // The state change (append attempt) and the outbox
        // publish are NOT atomic — see the dual-write window note on
        // `AdmitTaskHandler.HandleAsync`. Both commit in two
        // transactions (store SaveChanges first, then the IOutbox
        // seam publishes through the host-bridged transaction). A
        // crash between them leaves the attempt ledger advanced
        // without an emitted attempt-requested event; the engine's
        // inbox-claim is the dedupe seam that catches the re-issued
        // dispatch on the next operator-initiated retry.
        await store.SaveAsync(task, cancellationToken);

        if (!wasReplay)
        {
            var payload = new WorkTaskEvent(
                task.Id.Value,
                task.ProjectId.Value,
                task.Status.Value,
                task.AttemptOrdinal.Value,
                task.ActiveAttemptId?.Value,
                task.BriefVersion,
                task.ResolutionOutcome?.Value,
                task.MissionId?.Value.ToString(),
                clock.GetUtcNow());
            await outbox.PublishAsync(WorkTaskEventTypes.AttemptRequested, payload, cancellationToken);
        }

        logger.LogInformation(
            "DispatchRun for Task {TaskId} attempt {AttemptOrdinal} (replay={WasReplay})",
            task.Id, ordinal.Value, wasReplay);
        return new DispatchRunOutcome(ordinal.Value, wasReplay);
    }
}
