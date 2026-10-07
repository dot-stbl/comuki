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
/// Merge-queue read-side: enqueue, paged list, and atomic claim-next
/// (via the guarded raw-SQL claim path). The transition actions
/// (claim / release / merge / abandon / annotate) are owned by
/// per-verb handlers in <see cref="Claim"/>,
/// <see cref="Release"/>, <see cref="MergeEntry"/>,
/// <see cref="Abandon"/>, <see cref="Annotate"/> — each handler has
/// its own command and validator, so each is independently
/// validatable and auditable.
/// </summary>
/// <param name="store">EF-backed merge-queue store (scoped unit-of-work carrier).</param>
/// <param name="validator">FluentValidation for the enqueue command.</param>
/// <param name="journal">Run-journal writer — the enqueue path stamps <c>merge_queue.run_referenced</c> in the same transaction as the row insert when a <see cref="RunId"/> is supplied.</param>
/// <param name="db">Orchestration EF context of the current scope — the enqueue path opens a transaction here so the store's <c>SaveChangesAsync</c> and the journal's <c>SaveChangesAsync</c> enlist on the same connection. Both <c>Comuki.Engine.Orchestration.Infrastructure.Persistence.Stores.MergeQueueStoreEf</c> and <see cref="RunJournalEf"/> are scoped to this same instance, so the transactional contract is real (mirrors the <c>Comuki.Engine.Orchestration.Infrastructure.Queue.WorkItemQueueEf</c> claim/terminalization pattern).</param>
/// <param name="clock">Wall-clock for the create stamp.</param>
/// <param name="logger">Structured logger.</param>
public sealed class MergeQueueService(
    IMergeQueueStore store,
    IValidator<EnqueueMergeRequestCommand> validator,
    IRunJournal journal,
    OrchestrationDbContext db,
    TimeProvider clock,
    ILogger<MergeQueueService> logger)
{
    /// <summary>Enqueues a new entry; returns the post-create view (id, default Pending status).</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    public async Task<MergeQueueEntryView> EnqueueAsync(EnqueueMergeRequestCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var entry = MergeQueueEntry.Create(
            command.ProjectId,
            command.BranchName,
            command.PullRequestUrl,
            command.ConflictResolution,
            command.Notes,
            clock.GetUtcNow(),
            command.RunId);

        // The run-referenced event lives in the same transaction as
        // the row insert — a late emit (autocommit scope after
        // CommitAsync) would let a reader see the row before the
        // journal sees the link, which is the half-stamped-event
        // trap documented in add-orchestra §3 — Coda. Cross-project
        // release trains (RunId == null) skip the emit by design.
        // The transaction is committed even on the RunId-is-null
        // path: the store's SaveChangesAsync runs in autocommit by
        // default, and rolling an explicit transaction for a
        // single SaveChangesAsync changes the connection-level
        // semantics (BeginTransactionAsync on Npgsql pins a snapshot
        // reader) for no real benefit. We commit unconditionally
        // because the journal append is conditional — the
        // conditional branch never opens the transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await store.AddAsync(entry, cancellationToken);

        if (command.RunId is { } runId)
        {
            var entryEvent = new RunEventEntry(
                Id: Guid.NewGuid(),
                RunId: runId,
                Type: RunEventTypes.MergeQueueRunReferenced,
                PayloadJson: WorkItemEventPayloads.MergeQueueRunReferenced(
                    kind: "entry",
                    rowId: entry.Id,
                    runId: runId.Value),
                OccurredAt: clock.GetUtcNow());
            await journal.AppendAsync(entryEvent, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Enqueued merge-queue entry {EntryId} for branch {BranchName} project {ProjectId}",
            entry.Id,
            entry.BranchName,
            entry.ProjectId);
        return MergeQueueEntryView.FromEntry(entry);
    }

    /// <summary>Paged list within optional status / project scope; newest enqueues first.</summary>
    /// <param name="status"></param>
    /// <param name="projectId"></param>
    /// <param name="limit"></param>
    /// <param name="offset"></param>
    /// <param name="cancellationToken"></param>
    public async Task<MergeQueuePage> ListAsync(
        MergeQueueStatus? status,
        ProjectId? projectId,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var entries = await store.ListAsync(status, projectId, limit, offset, cancellationToken);
        var views = new List<MergeQueueEntryView>(entries.Count);
        foreach (var entry in entries)
        {
            views.Add(MergeQueueEntryView.FromEntry(entry));
        }

        return new MergeQueuePage { Items = views, Total = views.Count };
    }

    /// <summary>Atomically claims the oldest pending entry in the requested scope.</summary>
    /// <param name="projectId"></param>
    /// <param name="operatorId"></param>
    /// <param name="cancellationToken"></param>
    public async Task<MergeQueueEntryView?> ClaimNextAsync(
        ProjectId? projectId,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            throw new ArgumentException("operator id must not be empty", nameof(operatorId));
        }

        var claimed = await store.ClaimNextAsync(projectId, operatorId, clock.GetUtcNow(), cancellationToken);
        return claimed is null ? null : MergeQueueEntryView.FromEntry(claimed);
    }
}
