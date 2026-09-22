using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.Annotate;

/// <summary>
/// Updates free-text notes on the entry (no status change). Returns
/// the post-annotation view, or null when the entry id is unknown.
/// </summary>
/// <param name="store"></param>
/// <param name="validator"></param>
/// <param name="logger"></param>
public sealed class AnnotateMergeQueueHandler(
    IMergeQueueStore store,
    IValidator<AnnotateMergeQueueCommand> validator,
    ILogger<AnnotateMergeQueueHandler> logger)
{
    /// <summary>Validates and executes the annotation.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ValidationException">structural validation failed.</exception>
    public async Task<MergeQueueEntryView?> HandleAsync(AnnotateMergeQueueCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var entry = await store.FindByIdAsync(command.EntryId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        entry.SetNotes(command.Notes);
        await store.SaveAsync(entry, cancellationToken);

        logger.LogInformation(
            "Merge-queue entry {EntryId} notes updated",
            entry.Id);
        return MergeQueueEntryView.FromEntry(entry);
    }
}
