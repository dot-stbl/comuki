using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Workers.Read;

/// <summary>
/// EF derivation of the worker registry — extracted from
/// <see cref="WorkersReadHandler"/> so the handler holds only paging and
/// the query logic is testable in isolation
/// (<c>code-shape.md</c> §1a). Busy / offline rows come from live
/// work-item leases; idle rows from each worker's most recent claim in
/// the journal window.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="lease">Lease policy — the busy/offline stale boundary.</param>
/// <param name="clock">Read time.</param>
public sealed class WorkersReadQuery(
    OrchestrationDbContext db,
    IOptions<LeaseOptions> lease,
    TimeProvider clock)
{
    /// <summary>How many recent status-change journal rows are scanned for idle workers' last claim.</summary>
    public const int JournalScanTake = 200;

    /// <summary>Derives workers: every live lease first (newest heartbeat), then journal-idle workers by recency, paged.</summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="singleWorker">When set, restricts both halves to one worker (the by-id read).</param>
    /// <param name="cancellationToken">Cooperative cancellation for both queries.</param>
    public async Task<WorkersDerivation> DeriveAsync(
        int page,
        int pageSize,
        WorkerId? singleWorker,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var staleAfter = lease.Value.LeaseTtl + lease.Value.ReapGrace;

        var busyItems = await db.WorkItems.AsNoTracking()
            .Where(item => item.Status == WorkItemStatus.Running && item.LeasedBy != null)
            .Where(item => singleWorker == null || item.LeasedBy == singleWorker)
            .OrderByDescending(item => item.HeartbeatAt)
            .Select(item => new
            {
                item.Id,
                item.RunId,
                item.LeasedBy,
                item.ProfileKey,
                item.Image,
                item.LeaseUntil,
                item.HeartbeatAt,
                item.Attempt,
            })
            .ToListAsync(cancellationToken);

        var busyRunIds = busyItems.Select(static item => item.RunId).Distinct().ToList();
        var projectByRunId = await db.Runs.AsNoTracking()
            .Where(run => busyRunIds.Contains(run.Id))
            .ToDictionaryAsync(run => run.Id, run => run.ProjectId, cancellationToken);

        var busyRows = busyItems.ConvertAll(item => new WorkerView(
            item.LeasedBy!.Value.Value,
            WorkerStatusRules.DeriveState(isBusy: true, item.HeartbeatAt ?? now, staleAfter, now),
            projectByRunId.TryGetValue(item.RunId, out var projectId) ? projectId.Value : null,
            item.ProfileKey,
            item.Image,
            item.Id,
            item.RunId.Value,
            item.LeaseUntil,
            item.HeartbeatAt,
            item.Attempt,
            item.HeartbeatAt ?? now));

        var lastSeenAtByWorkerId = await ReadRecentClaimWorkersAsync(cancellationToken);

        var idleRows = lastSeenAtByWorkerId
            .Where(pair => singleWorker == null || pair.Key == singleWorker.Value)
            .Where(pair => busyRows.All(row => row.WorkerId != pair.Key.Value))
            .Select(pair => new WorkerView(
                pair.Key.Value,
                WorkerStatusRules.DeriveState(isBusy: false, pair.Value, staleAfter, now),
                ProjectId: null,
                ProfileKey: null,
                Image: null,
                CurrentWorkItemId: null,
                CurrentRunId: null,
                LeaseUntil: null,
                HeartbeatAt: null,
                Attempt: 0,
                pair.Value))
            .OrderByDescending(row => row.LastSeenAt)
            .ToList();

        var merged = busyRows
            .OrderByDescending(row => row.LastSeenAt)
            .Concat(idleRows)
            .ToList();

        var total = merged.Count;
        var paged = merged.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new WorkersDerivation(paged, total);
    }

    /// <summary>
    /// Reads the last-claim time per worker from the recent
    /// <c>work_item.status_changed</c> journal window — the idle half's
    /// source. The runs join is the subject-scope gate: the journal
    /// table itself carries no scope filter, so events are admitted
    /// only when their run passes the context's global run filter
    /// (out-of-scope projects are absent from the idle half, same
    /// contract as the runs surface).
    /// </summary>
    /// <param name="cancellationToken">Cooperative cancellation for the journal query.</param>
    public async Task<IReadOnlyList<KeyValuePair<WorkerId, DateTimeOffset>>> ReadRecentClaimWorkersAsync(CancellationToken cancellationToken)
    {
        var events = await db.RunEvents.AsNoTracking()
            .Where(journalEvent => journalEvent.Type == RunEventTypes.WorkItemStatusChanged)
            .Where(journalEvent => db.Runs.Any(run => run.Id == journalEvent.RunId))
            .OrderByDescending(journalEvent => journalEvent.OccurredAt)
            .Take(JournalScanTake)
            .Select(journalEvent => new { journalEvent.Payload, journalEvent.OccurredAt })
            .ToListAsync(cancellationToken);

        var lastSeenAtByWorkerId = new Dictionary<WorkerId, DateTimeOffset>();
        foreach (var journalEvent in events)
        {
            if (WorkerStatusRules.ParseClaimWorker(journalEvent.Payload) is not { } claimedBy)
            {
                continue;
            }

            if (!lastSeenAtByWorkerId.TryAdd(claimedBy, journalEvent.OccurredAt)
                && lastSeenAtByWorkerId[claimedBy] < journalEvent.OccurredAt)
            {
                lastSeenAtByWorkerId[claimedBy] = journalEvent.OccurredAt;
            }
        }

        return [.. lastSeenAtByWorkerId];
    }
}
