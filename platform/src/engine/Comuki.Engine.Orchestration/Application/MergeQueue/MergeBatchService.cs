using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>
/// Merge-batch read-side: create + paged list. Transition actions
/// (claim / merge / abandon) are owned by per-verb handlers in
/// <see cref="BatchClaim"/>, <see cref="BatchMerge"/>,
/// <see cref="BatchAbandon"/> — each handler has its own command and
/// validator, so each is independently validatable and auditable.
/// The batch list is not a queue — claim-next is on the
/// <see cref="MergeQueueService"/> side; here we only orchestrate the
/// batch aggregate itself.
/// </summary>
/// <param name="store"></param>
/// <param name="validator"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class MergeBatchService(
    IMergeBatchStore store,
    IValidator<CreateMergeBatchCommand> validator,
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

        return new MergeBatchPage { Items = views, Total = views.Count };
    }
}
