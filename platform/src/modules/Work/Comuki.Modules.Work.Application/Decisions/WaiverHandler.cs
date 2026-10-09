using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Decisions;
using Comuki.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Application.Decisions;

/// <summary>Waiver handler — Blocked → Resolved with outcome <c>Waived</c>. Distinct-human approval enforced upstream (Identity).</summary>
public sealed class WaiverHandler(
    IWorkTaskStore store,
    IOutbox outbox,
    TimeProvider clock,
    ILogger logger) : WorkDecisionHandlerBase(store, outbox, clock, logger)
{
    public override DecisionKind Kind => DecisionKind.Waiver;

    public override async Task HandleAsync(WorkDecideCommand command, CancellationToken cancellationToken)
    {
        var task = await LoadOrThrowAsync(command.TaskId, cancellationToken);
        var now = Clock.GetUtcNow();

        task.TransitionTo(WorkTaskStatus.Resolved, WorkTaskResolutionOutcome.Waived, now);

        await PersistAndPublishAsync(task, WorkTaskEventTypes.Resolved, cancellationToken);
        Logger.LogInformation(
            "Waiver decision for Task {TaskId} by actor {ActorId}; status flipped Blocked → Resolved (Waived)",
            task.Id, command.Decision.ActorId);
    }
}
