using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>
/// Merge-batch orchestration: create, list, transition by id (claim /
/// merge / abandon). Domain factory enforces status invariants
/// (illegal transitions throw) and the validator catches structural
/// mistakes before they hit the store. The batch list is not a queue —
/// claim-next is on the <see cref="MergeQueueService"/> side; here we
/// only orchestrate the batch aggregate itself.
/// </summary>
/// <param name="store"></param>
/// <param name="validator"></param>
/// <param name="updateValidator"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class MergeBatchService(
    IMergeBatchStore store,
    IValidator<CreateMergeBatchCommand> validator,
    IValidator<UpdateMergeBatchCommand> updateValidator,
    TimeProvider clock,
    ILogger<MergeBatchService> logger)
{
    /// <summary>Creates a new batch; returns the post-create view.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    public async Task<MergeBatchView> CreateAsync(CreateMergeBatchCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var batch = MergeBatch.Create(command.Name, command.PullRequestUrls, clock.GetUtcNow());

        await store.AddAsync(batch, cancellationToken);
        logger.LogInformation(
            "Created merge-batch {BatchId} '{BatchName}' with {UrlCount} PR urls",
            batch.Id,
            batch.Name,
            batch.PullRequestUrls.Count);
        return MergeBatchView.FromBatch(batch);
    }

    /// <summary>Paged list within optional status scope; newest batches first.</summary>
    /// <param name="status"></param>
    /// <param name="limit"></param>
    /// <param name="offset"></param>
    /// <param name="cancellationToken"></param>
    public async Task<MergeBatchPage> ListAsync(
        MergeBatchStatus? status,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var batches = await store.ListAsync(status, limit, offset, cancellationToken);
        var views = new List<MergeBatchView>(batches.Count);
        foreach (var batch in batches)
        {
            views.Add(MergeBatchView.FromBatch(batch));
        }

        return new MergeBatchPage(views, views.Count);
    }

    /// <summary>
    /// Dispatches one PATCH command: Claim / Merge / Abandon. Returns
    /// the post-action view, or null when the batch id is unknown.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidOperationException">the batch is in a status the action cannot run from.</exception>
    public async Task<MergeBatchView?> UpdateAsync(UpdateMergeBatchCommand command, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(command, cancellationToken);

        var batch = await store.FindByIdAsync(command.BatchId, cancellationToken);
        if (batch is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        switch (command.Action)
        {
            case MergeBatchAction.Claim:
                batch.Claim();
                break;
            case MergeBatchAction.Merge:
                batch.MarkMerged(now);
                break;
            case MergeBatchAction.Abandon:
                batch.MarkAbandoned(command.Reason!, now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Action, "unknown merge-batch action");
        }

        await store.SaveAsync(batch, cancellationToken);
        logger.LogInformation(
            "Merge-batch {BatchId} transitioned via {Action}",
            batch.Id,
            command.Action);
        return MergeBatchView.FromBatch(batch);
    }
}
