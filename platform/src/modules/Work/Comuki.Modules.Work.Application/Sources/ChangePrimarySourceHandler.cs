using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain.Exceptions;
using Comuki.Modules.Work.Domain.Ids;

namespace Comuki.Modules.Work.Application.Sources;

/// <summary>
/// <c>Work.ChangePrimarySource</c> command — the umbrella's task 7.2 path.
/// Promotes an existing related source to primary; the previous primary
/// becomes a related source with a link note describing the demotion.
/// The new primary receives lifecycle sync events; the demoted primary
/// and other related sources receive key-Decision summaries only.
/// </summary>
public sealed record ChangePrimarySourceCommand(WorkTaskId TaskId, Guid NewPrimarySourceId);

/// <summary>Handler for <see cref="ChangePrimarySourceCommand"/>.</summary>
public sealed class ChangePrimarySourceHandler(IWorkTaskStore store, TimeProvider clock)
{
    /// <summary>Handles the command — single-row update on the aggregate, no outbox event.</summary>
    public async Task HandleAsync(ChangePrimarySourceCommand command, CancellationToken cancellationToken = default)
    {
        var task = await store.FindAsync(command.TaskId, cancellationToken)
            ?? throw new WorkTaskDomainException(
                WorkTaskErrorCodes.TaskNotFound,
                $"task {command.TaskId} not found");

        task.ChangePrimarySource(command.NewPrimarySourceId, clock.GetUtcNow());

        await store.SaveAsync(task, cancellationToken);
    }
}
