using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Orchestration.Application.MergeQueue.BatchClaim;

/// <summary>
/// Claims a merge batch: loads the row, calls the domain mutator
/// (which enforces the Pending precondition), persists. Returns the
/// post-claim view, or null when the batch id is unknown.
/// </summary>
/// <param name="store"></param>
/// <param name="validator"></param>
/// <param name="logger"></param>
public sealed class ClaimMergeBatchHandler(
    IMergeBatchStore store,
    IValidator<ClaimMergeBatchCommand> validator,
    ILogger<ClaimMergeBatchHandler> logger)
{
    /// <summary>Validates and executes the claim.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ValidationException">structural validation failed.</exception>
    public async Task<MergeBatchView?> HandleAsync(ClaimMergeBatchCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var batch = await store.FindByIdAsync(command.BatchId, cancellationToken);
        if (batch is null)
        {
            return null;
        }

        batch.Claim();
        await store.SaveAsync(batch, cancellationToken);

        logger.LogInformation(
            "Merge-batch {BatchId} claimed",
            batch.Id);
        return MergeBatchView.FromBatch(batch);
    }
}
