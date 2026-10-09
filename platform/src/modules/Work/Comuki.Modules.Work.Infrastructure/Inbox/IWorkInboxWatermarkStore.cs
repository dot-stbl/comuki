namespace Comuki.Modules.Work.Infrastructure.Inbox;

/// <summary>
/// Per-(type, key) watermark for the engine outbox poller. The
/// Work-side subscribers (WorkDispatchRequestedSubscriber,
/// WorkAttemptCancelledSubscriber, WorkIngestRunTerminalSubscriber)
/// poll the <c>orchestration.outbox_messages</c> table by
/// <c>type IN (...)</c> and a last-seen id watermark
/// (<c>OutboxMessage.Id</c>, UUIDv7). The watermark store is the
/// seam that lets the poll survive a host restart without
/// re-processing every historical row (the
/// <c>dispatched_at IS NULL</c> predicate the brief warns against
/// is no good here — the NoopOutboxPublisher stamps every row as
/// dispatched every 5 s, so the simple "is null" check loses
/// every row).
/// <para>
/// The interface lives in Infrastructure; the in-memory
/// implementation is for the unit tests in
/// <c>Comuki.Modules.Work.Unit</c>. The persistent implementation
/// lives in the same Infrastructure project (the watermark is a
/// per-type Guid row, the <c>work.outbox_watermarks</c> table
/// that landed in task 3.2 of the change).
/// </para>
/// <para>
/// Both operations are <c>async</c> because the store is
/// resolved from a per-cycle DI scope (the subscribers are
/// hosted singletons; the prior sync contract was leaking the
/// scoped <c>WorkDbContext</c> as a captive dependency into the
/// worker's lifetime — <c>Record</c> without <c>SaveChanges</c>
/// and a <c>Get</c> <see cref="Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking"/>
/// against a long-lived instance). The record side writes a
/// single <c>INSERT … ON CONFLICT … DO UPDATE GREATEST(...)</c>
/// statement so the monotonic-direction contract is the SQL
/// engine's, not ours.
/// </para>
/// </summary>
public interface IWorkInboxWatermarkStore
{
    /// <summary>
    /// Returns the last-seen outbox id for the supplied
    /// <paramref name="type"/>. <see cref="Guid.Empty"/> when no row
    /// has been seen yet (the consumer must start at the head of
    /// the table).
    /// </summary>
    public Task<Guid> GetAsync(string type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records <paramref name="lastSeenId"/> as the consumer's
    /// progress for <paramref name="type"/>. Idempotent on the
    /// monotonic direction — a smaller id never replaces a
    /// larger one (the <c>GREATEST(...)
    /// </c> is the engine-side check so a re-processing retry can
    /// move the watermark backward only on explicit intent).
    /// </summary>
    public Task RecordAsync(string type, Guid lastSeenId, CancellationToken cancellationToken = default);
}
