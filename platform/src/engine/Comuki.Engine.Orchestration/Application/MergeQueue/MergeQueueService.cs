using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>
/// Merge-queue orchestration: enqueue, list, atomic claim (via the
/// guarded raw-SQL claim), transition by id (claim / release / merge /
/// abandon / annotate). Domain factory enforces status invariants
/// (illegal transitions throw) and the validator catches structural
/// mistakes before they hit the store.
/// </summary>
/// <param name="store"></param>
/// <param name="validator"></param>
/// <param name="updateValidator"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class MergeQueueService(
    IMergeQueueStore store,
    IValidator<EnqueueMergeRequestCommand> validator,
    IValidator<UpdateMergeQueueCommand> updateValidator,
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
            clock.GetUtcNow());

        await store.AddAsync(entry, cancellationToken);
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

        return new MergeQueuePage(views, views.Count);
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

    /// <summary>
    /// Dispatches one PATCH command: Claim / Release / Merge / Abandon / Annotate. Returns
    /// the post-action view, or null when the entry id is unknown.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidOperationException">the entry is in a status the action cannot run from.</exception>
    public async Task<MergeQueueEntryView?> UpdateAsync(UpdateMergeQueueCommand command, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(command, cancellationToken);

        var entry = await store.FindByIdAsync(command.EntryId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        switch (command.Action)
        {
            case MergeQueueAction.Claim:
                entry.Claim(command.OperatorId!, now);
                break;
            case MergeQueueAction.Release:
                entry.Release();
                break;
            case MergeQueueAction.Merge:
                entry.MarkMerged(now);
                break;
            case MergeQueueAction.Abandon:
                entry.MarkAbandoned(command.Reason!, now);
                break;
            case MergeQueueAction.Annotate:
                entry.SetNotes(command.Notes);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Action, "unknown merge-queue action");
        }

        await store.SaveAsync(entry, cancellationToken);
        logger.LogInformation(
            "Merge-queue entry {EntryId} transitioned via {Action} (operator {OperatorId})",
            entry.Id,
            command.Action,
            command.OperatorId);
        return MergeQueueEntryView.FromEntry(entry);
    }
}
