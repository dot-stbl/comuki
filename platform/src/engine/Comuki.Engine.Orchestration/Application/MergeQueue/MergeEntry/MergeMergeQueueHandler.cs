using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.MergeEntry;

/// <summary>
/// Marks an in-progress entry as merged: loads the row, calls the
/// domain mutator (which stamps the merge timestamp), persists. The
/// domain enforces the in-progress precondition.
/// </summary>
/// <param name="store"></param>
/// <param name="validator"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class MergeMergeQueueHandler(
    IMergeQueueStore store,
    IValidator<MergeMergeQueueCommand> validator,
    TimeProvider clock,
    ILogger<MergeMergeQueueHandler> logger)
{
    /// <summary>Validates and executes the merge.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ValidationException">structural validation failed.</exception>
    public async Task<MergeQueueEntryView?> HandleAsync(MergeMergeQueueCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var entry = await store.FindByIdAsync(command.EntryId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        entry.MarkMerged(now);
        await store.SaveAsync(entry, cancellationToken);

        logger.LogInformation(
            "Merge-queue entry {EntryId} merged",
            entry.Id);
        return MergeQueueEntryView.FromEntry(entry);
    }
}
