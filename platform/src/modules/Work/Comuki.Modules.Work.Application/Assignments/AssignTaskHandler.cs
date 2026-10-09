using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain.Assignments;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Ids;

namespace Comuki.Modules.Work.Application.Assignments;

/// <summary>
/// <c>Work.AssignTask</c> command — the umbrella's task 7.3 surface.
/// Adds a responsible-actor assignment row. Brain-assigned rows land
/// in <see cref="AssignmentProposalState.Direct"/> and are effective
/// immediately; Human-assigned rows land in
/// <see cref="AssignmentProposalState.Proposed"/> and require
/// <see cref="ApproveAssignmentCommand"/> by a distinct human to
/// become effective (no self-approval, task 7.5).
/// </summary>
public sealed record AssignTaskCommand(WorkTaskId TaskId, WorkTaskAssignment Assignment);

/// <summary>Handler for <see cref="AssignTaskCommand"/>.</summary>
public sealed class AssignTaskHandler(IWorkTaskStore store)
{
    /// <summary>Handles the command — single-row insert on the aggregate.</summary>
    public async Task HandleAsync(AssignTaskCommand command, CancellationToken cancellationToken = default)
    {
        var task = await store.FindAsync(command.TaskId, cancellationToken)
            ?? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TaskNotFound,
                $"task {command.TaskId} not found");

        task.AddAssignment(command.Assignment);

        await store.SaveAsync(task, cancellationToken);
    }
}

/// <summary>
/// <c>Work.ApproveAssignment</c> command — approves a Human-proposed
/// assignment by a distinct human (task 7.5: no self-approval).
/// </summary>
public sealed record ApproveAssignmentCommand(WorkTaskId TaskId, Guid AssignmentId, string ApproverActorId);

/// <summary>Handler for <see cref="ApproveAssignmentCommand"/>.</summary>
public sealed class ApproveAssignmentHandler(IWorkTaskStore store, TimeProvider clock)
{
    /// <summary>Handles the command — proposed → approved.</summary>
    public async Task HandleAsync(ApproveAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        var task = await store.FindAsync(command.TaskId, cancellationToken)
            ?? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TaskNotFound,
                $"task {command.TaskId} not found");

        task.ApproveAssignment(command.AssignmentId, command.ApproverActorId, clock.GetUtcNow());

        await store.SaveAsync(task, cancellationToken);
    }
}
