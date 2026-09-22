using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.Release;

/// <summary>
/// Releases an in-progress claim back to pending. The domain mutator
/// enforces that the entry is currently claimed — anything else throws.
/// Returns the post-release view, or null when the entry id is unknown.
/// </summary>
/// <param name="store"></param>
/// <param name="validator"></param>
/// <param name="logger"></param>
public sealed class ReleaseMergeQueueHandler(
    IMergeQueueStore store,
    IValidator<ReleaseMergeQueueCommand> validator,
    ILogger<ReleaseMergeQueueHandler> logger)
{
    /// <summary>Validates and executes the release.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ValidationException">structural validation failed.</exception>
    public async Task<MergeQueueEntryView?> HandleAsync(ReleaseMergeQueueCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var entry = await store.FindByIdAsync(command.EntryId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        entry.Release();
        await store.SaveAsync(entry, cancellationToken);

        logger.LogInformation(
            "Merge-queue entry {EntryId} released",
            entry.Id);
        return MergeQueueEntryView.FromEntry(entry);
    }
}
