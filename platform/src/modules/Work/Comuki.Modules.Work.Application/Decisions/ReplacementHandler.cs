using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain.Decisions;
using Comuki.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Application.Decisions;

/// <summary>Replacement handler — the current Task resolves with outcome <c>Replaced</c>; the inbound id's authoritative Task is now the new one (handled by the caller).</summary>
public sealed class ReplacementHandler(
    IWorkTaskStore store,
    IOutbox outbox,
    TimeProvider clock,
    ILogger logger) : WorkDecisionHandlerBase(store, outbox, clock, logger)
{
    public override DecisionKind Kind => DecisionKind.Replacement;

    public override async Task HandleAsync(WorkDecideCommand command, CancellationToken cancellationToken)
    {
        var task = await LoadOrThrowAsync(command.TaskId, cancellationToken);
        var now = Clock.GetUtcNow();

        // Aggregate guard: the brief on the replacement must be non-empty.
        task.StampReplacementOutcome(now);

        await PersistAndPublishAsync(task, WorkTaskEventTypes.Resolved, cancellationToken);
        Logger.LogInformation(
            "Replacement decision for Task {TaskId} by actor {ActorId}; the legacy Task is read-only after replacement",
            task.Id, command.Decision.ActorId);
    }
}
