namespace Comuki.Modules.Work.Infrastructure.Persistence.Entities;

/// <summary>
/// EF entity for <c>work.outbox_watermarks</c> — one row per
/// (subscriber, type). The Work-side outbox poller
/// (<c>WorkOutboxPollDispatcher</c>) reads engine outbox rows with
/// <c>id &gt; last_seen_id</c> for the supplied types; this row
/// is the durable progress signal. The NoopOutboxPublisher
/// stamps every engine outbox row as dispatched every 5 s, so the
/// "WHERE dispatched_at IS NULL" predicate is no good here — the
/// watermark is the only honest progress signal (per
/// <c>WorkDispatchRequestedSubscriber</c> remarks).
/// <para>
/// The <c>LastSeenId</c> is a <see cref="Guid"/> (UUIDv7) — the
/// engine's <c>outbox_messages.id</c> is a Guid, and Postgres
/// compares uuid &lt; uuid natively. The brief originally carried
/// a <c>bigint</c> watermark against the int legacy of the engine
/// journal, but Заход 3 standardized on UUIDv7 PKs across the
/// engine — the Work watermark followed.
/// </para>
/// </summary>
public sealed record OutboxWatermarkEntity(
    string Subscriber,
    string Type,
    Guid LastSeenId,
    DateTimeOffset UpdatedAt)
{
    public OutboxWatermarkEntity() : this(
        Subscriber: string.Empty,
        Type: string.Empty,
        LastSeenId: Guid.Empty,
        UpdatedAt: default)
    {
    }
}
