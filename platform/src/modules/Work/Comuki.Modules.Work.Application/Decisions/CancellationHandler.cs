using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Decisions;
using Comuki.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Application.Decisions;

/// <summary>
/// Cancellation handler — any non-terminal status → Cancelled.
/// Distinct from <see cref="Cancellation.CancelAttemptHandler"/>,
/// which cancels a single attempt (Task stays in its current
/// status); this handler drives a terminal Task cancellation.
/// </summary>
public sealed class CancellationHandler(
    IWorkTaskStore store,
    IOutbox outbox,
    TimeProvider clock,
    ILogger logger) : WorkDecisionHandlerBase(store, outbox, clock, logger)
{
    public override DecisionKind Kind => DecisionKind.Cancellation;

    public override async Task HandleAsync(WorkDecideCommand command, CancellationToken cancellationToken)
    {
        var task = await LoadOrThrowAsync(command.TaskId, cancellationToken);
        var now = Clock.GetUtcNow();

        task.TransitionTo(WorkTaskStatus.Cancelled, null, now);

        await PersistAndPublishAsync(task, WorkTaskEventTypes.Cancelled, cancellationToken);
        Logger.LogInformation(
            "Cancellation decision for Task {TaskId} by actor {ActorId}; status flipped → Cancelled",
            task.Id, command.Decision.ActorId);
    }
}
