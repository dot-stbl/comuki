using Comuki.Modules.Work.Infrastructure.Inbox;
using Comuki.Modules.Work.Infrastructure.Persistence.WatermarkSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Comuki.Modules.Work.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IWorkInboxWatermarkStore"/>
/// over <see cref="WorkDbContext"/>. The watermark row lives in
/// <c>work.outbox_watermarks</c> keyed on
/// <c>(subscriber, type)</c>. The contract is two async
/// methods — the prior sync / captive-dependency shape (a
/// stored <c>WorkDbContext</c> resolved at construction and
/// <c>Record</c> without <c>SaveChanges</c>) was the source of
/// the watermark-standing-still bug; resolving from a per-cycle
/// scope plus a single SQL upsert is the structural fix.
/// </summary>
/// <param name="clock">Time source for the <c>updated_at</c> stamp on the watermark row.</param>
public sealed class EfWorkInboxWatermarkStore(WorkDbContext db, TimeProvider clock) : IWorkInboxWatermarkStore
{
    /// <inheritdoc />
    public async Task<Guid> GetAsync(string type, CancellationToken cancellationToken = default)
    {
        return await db.OutboxWatermarks
            .AsNoTracking()
            .Where(wm => wm.Subscriber == DefaultSubscriber && wm.Type == type)
            .Select(static wm => (Guid?)wm.LastSeenId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? Guid.Empty;
    }

    /// <inheritdoc />
    public async Task RecordAsync(string type, Guid lastSeenId, CancellationToken cancellationToken = default)
    {
        // The raw ADO upsert needs an open connection — the EF context's
        // connection is closed between per-cycle scopes (the watermark
        // store is resolved from a per-cycle scope; the
        // <see cref="WorkDbContext"/> that owns the connection never
        // had OpenConnectionAsync called on it before Record). The
        // ref-counted Open/CloseConnection pairing mirrors the sibling
        // <see cref="EfWorkInbox"/> — both stores open the connection
        // through <c>db.Database</c> and close it in <c>finally</c> so a
        // future insert through the same context still sees the
        // transaction the host bound (when the host's BeginTransaction
        // wins, the inner connection is the same physical connection).
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await WatermarkStoreUpsert.UpsertAsync(
                db.Database.GetDbConnection(),
                db.Database.CurrentTransaction?.GetDbTransaction(),
                DefaultSubscriber,
                type,
                lastSeenId,
                clock.GetUtcNow(),
                cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private const string DefaultSubscriber = "work";
}
