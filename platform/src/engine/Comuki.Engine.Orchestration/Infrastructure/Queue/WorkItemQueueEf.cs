using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Outbox;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Comuki.Engine.Orchestration.Infrastructure.Queue;

/// <summary>
/// Postgres implementation of <see cref="IWorkItemQueue"/> on top of
/// <see cref="OrchestrationDbContext"/>: claim is a guarded
/// <c>UPDATE ... FOR UPDATE SKIP LOCKED ... RETURNING</c>, every mutation
/// carries its journal event in the same transaction. Misses are values.
/// </summary>
/// <param name="db">Orchestration EF context — the unit-of-work carrier for the queue's transactions.</param>
/// <param name="outbox">Outbox staging surface — terminal complete/fail transitions also stage an <c>orchestration.run.terminated.v1</c> message on the caller's scope (WS7, issue #87).</param>
public sealed class WorkItemQueueEf(OrchestrationDbContext db, IOutbox outbox) : IWorkItemQueue
{
    /// <inheritdoc />
    public async Task<ClaimedWorkItem?> ClaimAsync(
        WorkerId workerId,
        WorkItemLabels labels,
        DateTimeOffset leaseUntil,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await using var command = WorkItemQueueSql.CreateClaimCommand(transaction.GetDbTransaction(), workerId, labels, leaseUntil, now);

        ClaimedWorkItem? claimed = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                claimed = WorkItemQueueSql.ReadClaimed(reader);
            }
        }

        if (claimed is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        db.RunEvents.Add(RunEvent.Create(
            claimed.RunId,
            RunEventTypes.WorkItemStatusChanged,
            WorkItemEventPayloads.StatusChanged(
                claimed.WorkItemId,
                nameof(WorkItemStatus.Queued),
                nameof(WorkItemStatus.Running),
                workerId.Value,
                claimed.Attempt),
            now));

        // First claim of the run's items activates the run (Queued ->
        // Running); a guarded no-op when another worker claimed first.
        await RunProgression.ActivateAsync(db, transaction, claimed.RunId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claimed;
    }

    /// <inheritdoc />
    public async Task<bool> HeartbeatAsync(
        Guid workItemId,
        WorkerId workerId,
        int generation,
        DateTimeOffset leaseUntil,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await using var command = WorkItemQueueSql.CreateHeartbeatCommand(transaction.GetDbTransaction(), workItemId, workerId, generation, leaseUntil, now);

        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(
        Guid workItemId,
        WorkerId workerId,
        int generation,
        string resultJson,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            throw new ArgumentException("result must not be empty", nameof(resultJson));
        }

        // result is worker-produced JSON — embedded as a structured value, not a string
        return await WorkItemOwnedTransition.ApplyAsync(
            db, outbox, completing: true, workItemId, workerId, generation,
            JsonDocument.Parse(resultJson).RootElement.Clone(), now, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> FailAsync(
        Guid workItemId,
        WorkerId workerId,
        int generation,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("reason must not be empty", nameof(reason));
        }

        // reason is human text — embedded as a JSON string
        return await WorkItemOwnedTransition.ApplyAsync(
            db, outbox, completing: false, workItemId, workerId, generation, reason, now, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountQueuedAsync(string? profileKey = null, CancellationToken cancellationToken = default)
    {
        return await db.WorkItems.CountAsync(
            item => item.Status == WorkItemStatus.Queued && (profileKey == null || item.ProfileKey == profileKey),
            cancellationToken);
    }
}

/// <summary>
/// One complete/fail transition: guarded <c>UPDATE ... RETURNING run_id</c>, then the
/// journal event (with the result/reason embedded) in the same transaction.
/// </summary>
file static class WorkItemOwnedTransition
{
    public static async Task<bool> ApplyAsync(
        OrchestrationDbContext db,
        IOutbox outbox,
        bool completing,
        Guid workItemId,
        WorkerId workerId,
        int generation,
        object detail,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await using var command = completing
            ? WorkItemQueueSql.CreateCompleteCommand(transaction.GetDbTransaction(), workItemId, workerId, generation, now)
            : WorkItemQueueSql.CreateFailCommand(transaction.GetDbTransaction(), workItemId, workerId, generation, now);

        RunId? runId = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                runId = new RunId(reader.GetGuid(0));
            }
        }

        if (runId is not { } owner)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        db.RunEvents.Add(RunEvent.Create(
            owner,
            RunEventTypes.WorkItemStatusChanged,
            WorkItemEventPayloads.StatusChangedWithDetail(
                workItemId,
                nameof(WorkItemStatus.Running),
                completing ? nameof(WorkItemStatus.Succeeded) : nameof(WorkItemStatus.Failed),
                detail),
            now));

        // A succeeded item may unblock dependents whose full prerequisite set
        // has now reached Succeeded; a failed completion must not (WS1
        // acceptance: prerequisite failure does not auto-unblock — see
        // UnblockDependentsSql's doc comment).
        if (completing)
        {
            await RunProgression.UnblockDependentsAsync(transaction, workItemId, now, cancellationToken);
        }

        // A terminal item may have been the run's last open one — finalize
        // the run (Succeeded when nothing failed, Failed otherwise).
        await RunProgression.FinalizeAsync(db, outbox, transaction, owner, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
