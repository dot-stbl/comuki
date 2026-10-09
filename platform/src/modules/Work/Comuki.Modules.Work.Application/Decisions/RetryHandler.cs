using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Decisions;
using Comuki.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Application.Decisions;

/// <summary>Retry handler — on <see cref="WorkTaskStatus.Blocked"/> from exhaustion, the next attempt ordinal is appended.</summary>
public sealed class RetryHandler(
    IWorkTaskStore store,
    IOutbox outbox,
    TimeProvider clock,
    ILogger logger) : WorkDecisionHandlerBase(store, outbox, clock, logger)
{
    public override DecisionKind Kind => DecisionKind.Retry;

    public override async Task HandleAsync(WorkDecideCommand command, CancellationToken cancellationToken)
    {
        var task = await LoadOrThrowAsync(command.TaskId, cancellationToken);
        var now = Clock.GetUtcNow();

        // Blocked → Ready so the next AppendAttempt is legal; the
        // dispatch path picks the attempt ordinal + Run id.
        task.TransitionTo(WorkTaskStatus.Ready, null, now);

        await PersistAndPublishAsync(task, WorkTaskEventTypes.Readied, cancellationToken);
        Logger.LogInformation(
            "Retry decision for Task {TaskId} by actor {ActorId}; status flipped Blocked → Ready",
            task.Id, command.Decision.ActorId);
    }
}
