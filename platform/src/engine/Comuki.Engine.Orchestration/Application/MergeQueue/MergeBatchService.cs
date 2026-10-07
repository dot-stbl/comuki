using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
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
/// <param name="store">EF-backed merge-batch store (scoped unit-of-work carrier).</param>
/// <param name="validator">FluentValidation for the create command.</param>
/// <param name="journal">Run-journal writer — the create path stamps <c>merge_queue.run_referenced</c> in the same transaction as the row insert when a <see cref="RunId"/> is supplied.</param>
/// <param name="db">Orchestration EF context of the current scope — the create path opens a transaction here so the store's <c>SaveChangesAsync</c> and the journal's <c>SaveChangesAsync</c> enlist on the same connection. Both <c>Comuki.Engine.Orchestration.Infrastructure.Persistence.Stores.MergeBatchStoreEf</c> and <see cref="RunJournalEf"/> are scoped to this same instance, so the transactional contract is real (mirrors the <c>Comuki.Engine.Orchestration.Infrastructure.Queue.WorkItemQueueEf</c> claim/terminalization pattern).</param>
/// <param name="clock">Wall-clock for the create stamp.</param>
/// <param name="logger">Structured logger.</param>
public sealed class MergeBatchService(
    IMergeBatchStore store,
    IValidator<CreateMergeBatchCommand> validator,
    IRunJournal journal,
    OrchestrationDbContext db,
    TimeProvider clock,
    ILogger<MergeBatchService> logger)
{
    /// <summary>Creates a new batch; returns the post-create view.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    public async Task<MergeBatchView> CreateAsync(CreateMergeBatchCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var batch = MergeBatch.Create(command.Name, command.PullRequestUrls, clock.GetUtcNow(), command.RunId);

        // Same-transaction row + journal append — see
        // MergeQueueService.EnqueueAsync for the rationale (a row
        // visible before the link is a half-stamped event). The
        // transaction is committed unconditionally on the
        // RunId-is-null path too: a single SaveChangesAsync with
        // an empty journal branch benefits from a real transaction
        // only when the journal append runs; rolling it for the
        // empty branch changes the connection's snapshot reader for
        // no real benefit. We commit after the store either way
        // because the journal branch is the conditional.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await store.AddAsync(batch, cancellationToken);

        if (command.RunId is { } runId)
        {
            var batchEvent = new RunEventEntry(
                Id: Guid.NewGuid(),
                RunId: runId,
                Type: RunEventTypes.MergeQueueRunReferenced,
                PayloadJson: WorkItemEventPayloads.MergeQueueRunReferenced(
                    kind: "batch",
                    rowId: batch.Id,
                    runId: runId.Value),
                OccurredAt: clock.GetUtcNow());
            await journal.AppendAsync(batchEvent, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

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
