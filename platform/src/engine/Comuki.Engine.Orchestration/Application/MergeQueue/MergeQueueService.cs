using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Ports;
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
/// <param name="store"></param>
/// <param name="validator"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class MergeQueueService(
    IMergeQueueStore store,
    IValidator<EnqueueMergeRequestCommand> validator,
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
