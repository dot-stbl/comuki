using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.Claim;

/// <summary>
/// Claims a merge-queue entry: loads the row, calls the domain mutator
/// (which enforces the status pre-condition), and persists. The store is
/// the source of truth — the handler does not pre-check status. Returns
/// the post-claim view, or null when the entry id is unknown.
/// </summary>
/// <param name="store"></param>
/// <param name="validator"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class ClaimMergeQueueHandler(
    IMergeQueueStore store,
    IValidator<ClaimMergeQueueCommand> validator,
    TimeProvider clock,
    ILogger<ClaimMergeQueueHandler> logger)
{
    /// <summary>Validates and executes the claim.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ValidationException">structural validation failed.</exception>
    public async Task<MergeQueueEntryView?> HandleAsync(ClaimMergeQueueCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var entry = await store.FindByIdAsync(command.EntryId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        entry.Claim(command.OperatorId, now);
        await store.SaveAsync(entry, cancellationToken);

        logger.LogInformation(
            "Merge-queue entry {EntryId} claimed by operator {OperatorId}",
            entry.Id,
            command.OperatorId);
        return MergeQueueEntryView.FromEntry(entry);
    }
}
