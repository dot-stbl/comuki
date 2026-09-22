using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.BatchAbandon;

/// <summary>
/// Marks a batch as abandoned. The domain mutator enforces the
/// status precondition (Pending or InProgress) and rejects empty
/// reasons.
/// </summary>
/// <param name="store"></param>
/// <param name="validator"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class AbandonMergeBatchHandler(
    IMergeBatchStore store,
    IValidator<AbandonMergeBatchCommand> validator,
    TimeProvider clock,
    ILogger<AbandonMergeBatchHandler> logger)
{
    /// <summary>Validates and executes the abandon.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ValidationException">structural validation failed.</exception>
    public async Task<MergeBatchView?> HandleAsync(AbandonMergeBatchCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var batch = await store.FindByIdAsync(command.BatchId, cancellationToken);
        if (batch is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        batch.MarkAbandoned(command.Reason, now);
        await store.SaveAsync(batch, cancellationToken);

        logger.LogInformation(
            "Merge-batch {BatchId} abandoned: {Reason}",
            batch.Id,
            command.Reason);
        return MergeBatchView.FromBatch(batch);
    }
}
