using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Completion;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Work.Application.Completion;

/// <summary>
/// <c>Work.Resolve</c> command — the umbrella's task 8.2 surface: accepts a
/// Decision carrying the proposed outcome; checks the
/// <see cref="WorkTask.CompletionPolicy"/>'s
/// <see cref="EvidenceContract"/> (every required kind is present);
/// enforces reviewer separation at the aggregate guard (the same actor
/// who authored the prior terminal attempt cannot Resolve). A Resolve
/// without all required evidence is rejected with
/// <c>422 work.completion.evidence-incomplete</c>; a same-actor Resolve
/// is rejected with <c>409 work.completion.reviewer-separation</c>.
/// </summary>
public sealed record WorkResolveCommand(
    WorkTaskId TaskId,
    string AuthoringActorId,
    WorkTaskResolutionOutcome Outcome,
    IReadOnlySet<EvidenceKind> Evidence);

/// <summary>Handler for <see cref="WorkResolveCommand"/>.</summary>
public sealed class WorkResolveHandler(
    IWorkTaskStore store,
    IOutbox outbox,
    TimeProvider clock,
    ILogger<WorkResolveHandler> logger)
{
    /// <summary>Handles the command.</summary>
    /// <exception cref="WorkTaskDomainException">Evidence contract incomplete, reviewer separation violated, or no terminal attempt on record.</exception>
    public async Task HandleAsync(WorkResolveCommand command, CancellationToken cancellationToken = default)
    {
        var task = await store.FindAsync(command.TaskId, cancellationToken)
            ?? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TaskNotFound,
                $"task {command.TaskId} not found");

        // Evidence contract check (task 8.2): the policy on the
        // aggregate decides what kinds the Resolve must carry. A
        // Resolve without every required kind is rejected.
        if (!task.CompletionPolicy.Contract.IsSatisfiedBy(command.Evidence))
        {
            throw new WorkTaskDomainException(
                WorkTaskErrorCodes.CompletionEvidenceIncomplete,
                $"resolve evidence {string.Join(", ", command.Evidence)} does not satisfy the task's policy {task.CompletionPolicy.Contract.RequiredKinds.Count} required kind(s)");
        }

        // Reviewer separation (task 8.3): the same actor who authored
        // the prior terminal attempt cannot Resolve. Aggregate guard
        // surfaces the rejection and records the audit row.
        task.EnsureReviewerSeparation(command.AuthoringActorId);

        var now = clock.GetUtcNow();
        task.TransitionTo(WorkTaskStatus.Resolved, command.Outcome, now);

        await store.SaveAsync(task, cancellationToken);

        var payload = new WorkTaskEvent(
            task.Id.Value,
            task.ProjectId.Value,
            task.Status.Value,
            task.AttemptOrdinal.Value,
            task.ActiveAttemptId?.Value,
            task.BriefVersion,
            task.ResolutionOutcome?.Value,
            task.MissionId?.Value.ToString(),
            now);
        await outbox.PublishAsync(WorkTaskEventTypes.Resolved, payload, cancellationToken);

        logger.LogInformation(
            "Work.Resolve for Task {TaskId} by actor {AuthoringActorId}; outcome {Outcome}",
            task.Id, command.AuthoringActorId, command.Outcome);
    }
}
