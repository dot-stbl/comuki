using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Shared.Contracts;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Application.Cancellation;

/// <summary>
/// <c>Work.CancelAttempt</c> command — emits the
/// <see cref="WorkAttemptCancelledEvent"/> envelope so
/// <c>WorkAttemptCancelledSubscriber</c> can forward the
/// cancellation to the engine-side cancel port
/// (<c>IWorkCancelPort</c>, host-closed in
/// <c>HostCancelRunAdapter</c>). The envelope carries the
/// cancelled <see cref="RunId"/> explicitly because the Task's
/// <see cref="Domain.WorkTask.ActiveAttemptId"/> is cleared in
/// the same transaction — without the explicit field the
/// subscriber would observe a
/// <see cref="WorkAttemptCancelledEvent.RunId"/> of
/// <c>Guid.Empty</c> on every row, skip every row as poison, and
/// the engine-side Run would never see a cancellation. The Task
/// stays in its current status (Ready / Active / Blocked); a
/// retry path can dispatch a fresh attempt.
/// </summary>
public sealed record CancelAttemptCommand(WorkTaskId TaskId, RunId RunId);

/// <summary>Handler for <see cref="CancelAttemptCommand"/>.</summary>
public sealed class CancelAttemptHandler(
    IWorkTaskStore store,
    IOutbox outbox,
    TimeProvider clock,
    ILogger<CancelAttemptHandler> logger)
{
    /// <summary>Handles the command.</summary>
    /// <exception cref="WorkTaskDomainException">The Task is missing, no active attempt, or the run id mismatches.</exception>
    public async Task HandleAsync(CancelAttemptCommand command, CancellationToken cancellationToken = default)
    {
        var task = await store.FindAsync(command.TaskId, cancellationToken)
            ?? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TaskNotFound,
                $"task {command.TaskId} not found");

        // Capture the cancelled Run id before clearing — the
        // envelope must carry it explicitly because
        // CompleteAttempt nulls ActiveAttemptId in this same call.
        var cancelledRunId = task.ActiveAttemptId?.Value
            ?? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.AttemptNotAllowed,
                $"task {command.TaskId} has no active attempt to cancel");

        // CompleteAttempt requires the run id to match the active
        // attempt; a mismatch throws.
        task.CompleteAttempt(command.RunId, clock.GetUtcNow());

        await store.SaveAsync(task, cancellationToken);

        var payload = new WorkAttemptCancelledEvent(
            task.Id.Value,
            task.ProjectId.Value,
            cancelledRunId,
            task.BriefVersion,
            clock.GetUtcNow());
        await outbox.PublishAsync(WorkTaskEventTypes.AttemptCancelled, payload, cancellationToken);

        logger.LogInformation(
            "CancelAttempt for Task {TaskId} run {RunId}; active attempt cleared",
            task.Id, command.RunId);
    }
}
