using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Engine.Orchestration.Infrastructure.Queue;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Orchestration.Infrastructure.Leases;

/// <summary>
/// Reaps work items whose lease expired past the grace window. Both requeue
/// (attempts left) and fail (budget exhausted) are set-based guarded SQL in
/// one transaction, each row journalled as <c>work_item.lease_expired</c> in
/// that same transaction. Guarded updates make the heartbeat/reaper race
/// safe: whoever's guard matches first wins in the store.
/// </summary>
/// <param name="db"></param>
/// <param name="clock"></param>
/// <param name="leaseOptions"></param>
public sealed class LeaseReaper(
    OrchestrationDbContext db,
    TimeProvider clock,
    IOptions<LeaseOptions> leaseOptions)
{
    /// <summary>Runs one reap sweep; safe to call concurrently and repeatedly.</summary>
    /// <param name="cancellationToken"></param>
    public async Task<IReadOnlyList<ReapedLease>> ReapAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var cutoff = now.Subtract(leaseOptions.Value.ReapGrace);
        var maxAttempts = leaseOptions.Value.MaxAttempts;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var dbTransaction = transaction.GetDbTransaction();

        var requeued = await LeaseReapSweep.ReadReapedAsync(
            WorkItemQueueSql.CreateReapRequeueCommand(dbTransaction, cutoff, maxAttempts, now),
            markedFailed: false,
            cancellationToken);
        var failed = await LeaseReapSweep.ReadReapedAsync(
            WorkItemQueueSql.CreateReapFailCommand(dbTransaction, cutoff, maxAttempts, now),
            markedFailed: true,
            cancellationToken);

        foreach (var lease in requeued)
        {
            db.RunEvents.Add(RunEvent.Create(
                lease.RunId,
                RunEventTypes.WorkItemLeaseExpired,
                WorkItemEventPayloads.LeaseExpired(lease.WorkItemId, nameof(WorkItemStatus.Queued), lease.Attempt),
                now));
        }

        foreach (var lease in failed)
        {
            db.RunEvents.Add(RunEvent.Create(
                lease.RunId,
                RunEventTypes.WorkItemLeaseExpired,
                WorkItemEventPayloads.LeaseExpired(lease.WorkItemId, nameof(WorkItemStatus.Failed), lease.Attempt),
                now));
        }

        if (requeued.Count + failed.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return [.. requeued, .. failed];
    }
}

/// <summary>Runs one reap statement and materialises its <c>RETURNING</c> rows.</summary>
file static class LeaseReapSweep
{
    public static async Task<List<ReapedLease>> ReadReapedAsync(
        System.Data.Common.DbCommand command,
        bool markedFailed,
        CancellationToken cancellationToken)
    {
        await using (command)
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            var reaped = new List<ReapedLease>();
            while (await reader.ReadAsync(cancellationToken))
            {
                reaped.Add(new ReapedLease(reader.GetGuid(0), new RunId(reader.GetGuid(1)), reader.GetInt32(2), markedFailed));
            }

            return reaped;
        }
    }
}
