using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Application.Decisions;

/// <summary>
/// Dispatcher for the <see cref="WorkDecideCommand"/>. Resolves the
/// Task from the store, picks the per-kind handler from the
/// registered <see cref="IWorkDecisionKindHandler"/> set by
/// <see cref="Domain.Decisions.DecisionKind"/>, and forwards the
/// command. Failures from the per-kind branches bubble out as
/// <see cref="WorkTaskDomainException"/>; the dispatch itself never
/// catches — the caller (the API endpoint) maps the typed exception
/// to ProblemDetails through the central handler
/// (<c>error-mapping.md</c> §4).
/// </summary>
public sealed class WorkDecideHandler(
    IWorkTaskStore store,
    IEnumerable<IWorkDecisionKindHandler> kindHandlers,
    ILogger<WorkDecideHandler> logger)
{
    /// <summary>Handles the command.</summary>
    /// <param name="command">The Work.Decide command (Task id + Decision payload).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="WorkTaskDomainException">The Task is missing, the decision kind has no registered handler, or the per-kind branch rejected the decision.</exception>
    public async Task HandleAsync(WorkDecideCommand command, CancellationToken cancellationToken = default)
    {
        var task = await store.FindAsync(command.TaskId, cancellationToken)
            ?? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TaskNotFound,
                $"task {command.TaskId} not found");

        IWorkDecisionKindHandler? handler = null;
        foreach (var candidate in kindHandlers)
        {
            if (candidate.Kind == command.Decision.Kind)
            {
                handler = candidate;
                break;
            }
        }

        if (handler is null)
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.IllegalTransition,
                $"no handler registered for decision kind '{command.Decision.Kind}'");
        }

        logger.LogDebug(
            "Routing Work.Decide Task {TaskId} → {Handler} (kind {Kind})",
            task.Id, handler.GetType().Name, command.Decision.Kind);

        await handler.HandleAsync(command, cancellationToken);
    }
}
