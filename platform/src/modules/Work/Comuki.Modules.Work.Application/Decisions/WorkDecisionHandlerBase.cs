using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Application.Decisions;

/// <summary>
/// Base for the deterministic decision handlers — owns the load +
/// save + outbox publish pattern. Per-kind subclasses override
/// <see cref="Kind"/> + <see cref="HandleAsync"/>; this base carries
/// the canonical load-publish path so a kind handler only writes
/// the domain mutation.
/// </summary>
public abstract class WorkDecisionHandlerBase(
    IWorkTaskStore store,
    IOutbox outbox,
    TimeProvider clock,
    ILogger logger) : IWorkDecisionKindHandler
{
    public abstract Domain.Decisions.DecisionKind Kind { get; }

    protected IWorkTaskStore Store { get; } = store;

    protected IOutbox Outbox { get; } = outbox;

    protected TimeProvider Clock { get; } = clock;

    protected ILogger Logger { get; } = logger;

    public abstract Task HandleAsync(WorkDecideCommand command, CancellationToken cancellationToken);

    /// <summary>Load the task or throw the canonical not-found error.</summary>
    protected async Task<WorkTask> LoadOrThrowAsync(WorkTaskId id, CancellationToken cancellationToken)
    {
        return await Store.FindAsync(id, cancellationToken)
            ?? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TaskNotFound,
                $"task {id} not found");
    }

    /// <summary>Persist the aggregate and publish a single WorkTaskEvent envelope.</summary>
    protected async Task PersistAndPublishAsync(WorkTask task, string eventType, CancellationToken cancellationToken)
    {
        await Store.SaveAsync(task, cancellationToken);

        var payload = new WorkTaskEvent(
            task.Id.Value,
            task.ProjectId.Value,
            task.Status.Value,
            task.AttemptOrdinal.Value,
            task.ActiveAttemptId?.Value,
            task.BriefVersion,
            task.ResolutionOutcome?.Value,
            task.MissionId?.Value.ToString(),
            Clock.GetUtcNow());
        await Outbox.PublishAsync(eventType, payload, cancellationToken);
    }
}
