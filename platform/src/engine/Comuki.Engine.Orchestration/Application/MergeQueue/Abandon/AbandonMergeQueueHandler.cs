using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.Abandon;

/// <summary>
/// Marks an entry as abandoned. Legal from Pending or InProgress (the
/// domain mutator enforces). Returns the post-abandon view, or null
/// when the entry id is unknown.
/// </summary>
/// <param name="store"></param>
/// <param name="validator"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class AbandonMergeQueueHandler(
    IMergeQueueStore store,
    IValidator<AbandonMergeQueueCommand> validator,
    TimeProvider clock,
    ILogger<AbandonMergeQueueHandler> logger)
{
    /// <summary>Validates and executes the abandon.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ValidationException">structural validation failed.</exception>
    public async Task<MergeQueueEntryView?> HandleAsync(AbandonMergeQueueCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var entry = await store.FindByIdAsync(command.EntryId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        entry.MarkAbandoned(command.Reason, now);
        await store.SaveAsync(entry, cancellationToken);

        logger.LogInformation(
            "Merge-queue entry {EntryId} abandoned: {Reason}",
            entry.Id,
            command.Reason);
        return MergeQueueEntryView.FromEntry(entry);
    }
}
