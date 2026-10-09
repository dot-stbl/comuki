using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Decisions;
using Comuki.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Application.Decisions;

/// <summary>Failed-resolution handler — Blocked → Resolved with outcome <c>Failed</c>. The only path to a Failed Task outcome.</summary>
public sealed class FailedResolutionHandler(
    IWorkTaskStore store,
    IOutbox outbox,
    TimeProvider clock,
    ILogger logger) : WorkDecisionHandlerBase(store, outbox, clock, logger)
{
    public override DecisionKind Kind => DecisionKind.FailedResolution;

    public override async Task HandleAsync(WorkDecideCommand command, CancellationToken cancellationToken)
    {
        var task = await LoadOrThrowAsync(command.TaskId, cancellationToken);
        var now = Clock.GetUtcNow();

        task.TransitionTo(WorkTaskStatus.Resolved, WorkTaskResolutionOutcome.Failed, now);

        await PersistAndPublishAsync(task, WorkTaskEventTypes.Resolved, cancellationToken);
        Logger.LogInformation(
            "Failed-resolution decision for Task {TaskId} by actor {ActorId}; status flipped Blocked → Resolved (Failed)",
            task.Id, command.Decision.ActorId);
    }
}
