using System.Text.Json;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Workers.Read;

/// <summary>
/// Wire row of one worker as the dashboard queue page sees it. Workers are
/// <b>derived</b>, not registered: a busy worker is a live work-item lease
/// (<c>leased_by</c> on a <c>Running</c> row); an idle worker is one whose
/// last claim still shows in the recent journal window. Fields only a
/// container runtime could answer (provider handle, uptime) are absent on
/// purpose — this host does not compose the compute engine.
/// </summary>
/// <param name="WorkerId">Worker id the lease / claim event names.</param>
/// <param name="State">Derived: <c>busy</c> (live lease, fresh heartbeat), <c>offline</c> (lease held but stale — reaper candidate), <c>idle</c> (recent claim, no live lease).</param>
/// <param name="ProjectId">Owning project (via the leased item's run); null for idle rows with no live lease.</param>
/// <param name="ProfileKey">Profile of the leased item; null when idle.</param>
/// <param name="Image">Image of the leased item; null when idle.</param>
/// <param name="CurrentWorkItemId">Item the worker holds; null when idle.</param>
/// <param name="CurrentRunId">Run of the leased item; null when idle.</param>
/// <param name="LeaseUntil">Lease expiry of the held item; null when idle.</param>
/// <param name="HeartbeatAt">Last heartbeat on the held item; null when idle.</param>
/// <param name="Attempt">Attempts of the held item; 0 when idle.</param>
/// <param name="LastSeenAt">Most recent of the worker's claim events, or the held item's heartbeat — whichever is later.</param>
public sealed record WorkerView(
    Guid WorkerId,
    string State,
    Guid? ProjectId,
    string? ProfileKey,
    string? Image,
    Guid? CurrentWorkItemId,
    Guid? CurrentRunId,
    DateTimeOffset? LeaseUntil,
    DateTimeOffset? HeartbeatAt,
    int Attempt,
    DateTimeOffset LastSeenAt);

/// <summary>One page of derived workers plus the paging envelope (same shape as the runs page).</summary>
/// <param name="Items">Page rows.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
/// <param name="Total">Total derived workers.</param>
public sealed record WorkersPage(
    IReadOnlyList<WorkerView> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>
/// Pure derivation rules behind <see cref="WorkerView.State"/> and the
/// journal-claim parsing — extracted so the EF handler holds only queries.
/// </summary>
public static class WorkerStatusRules
{
    /// <summary>Busy / offline boundary: a held lease whose heartbeat is older than the lease TTL (plus the reaper's grace) is a reaper candidate — the worker is offline, not busy.</summary>
    /// <param name="isBusy">The worker holds a <c>Running</c> lease.</param>
    /// <param name="lastSeenAt">Latest of heartbeat / claim time.</param>
    /// <param name="staleAfter">Lease TTL + reap grace, from <c>Orchestration:Lease</c>.</param>
    /// <param name="now">Read time.</param>
    public static string DeriveState(bool isBusy, DateTimeOffset lastSeenAt, TimeSpan staleAfter, DateTimeOffset now)
    {
        return !isBusy ? "idle" : now - lastSeenAt > staleAfter ? "offline" : "busy";
    }

    /// <summary>
    /// Parses one <c>work_item.status_changed</c> journal payload. Only
    /// claim events (<c>to == Running</c>) carry a <c>workerId</c>; every
    /// other shape (complete/fail detail events, reaper events) answers
    /// null so the caller skips the row without logging noise.
    /// </summary>
    /// <param name="payloadJson">Raw journal payload (<c>jsonb</c> column value).</param>
    public static WorkerId? ParseClaimWorker(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            return root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("to", out var to)
                || to.ValueKind != JsonValueKind.String
                || to.GetString() != "Running"
                || !root.TryGetProperty("workerId", out var workerId)
                || !workerId.TryGetGuid(out var parsed)
                ? null
                : new WorkerId(parsed);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
