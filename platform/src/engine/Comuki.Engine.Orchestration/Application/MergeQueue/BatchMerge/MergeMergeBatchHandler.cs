using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.BatchMerge;

/// <summary>
/// Marks an in-progress batch as merged. The domain mutator stamps
/// the merge timestamp; precondition (InProgress) is enforced by
/// the aggregate.
/// </summary>
/// <param name="store"></param>
/// <param name="validator"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class MergeMergeBatchHandler(
    IMergeBatchStore store,
    IValidator<MergeMergeBatchCommand> validator,
    TimeProvider clock,
    ILogger<MergeMergeBatchHandler> logger)
{
    /// <summary>Validates and executes the merge.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ValidationException">structural validation failed.</exception>
    public async Task<MergeBatchView?> HandleAsync(MergeMergeBatchCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var batch = await store.FindByIdAsync(command.BatchId, cancellationToken);
        if (batch is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        batch.MarkMerged(now);
        await store.SaveAsync(batch, cancellationToken);

        logger.LogInformation(
            "Merge-batch {BatchId} merged",
            batch.Id);
        return MergeBatchView.FromBatch(batch);
    }
}
