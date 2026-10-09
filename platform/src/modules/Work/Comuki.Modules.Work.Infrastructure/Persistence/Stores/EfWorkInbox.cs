using Comuki.Modules.Work.Application.Ports.Inbox;
using Comuki.Modules.Work.Infrastructure.Persistence.InboxSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Comuki.Modules.Work.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IWorkInbox"/> over
/// <see cref="WorkDbContext"/>. The dedupe ledger is a separate
/// <c>inbox_receipts</c> table whose PK is the message id —
/// the same WS9 / admission-claim pattern the engine uses.
/// The <c>INSERT … ON CONFLICT DO NOTHING</c> shape is a
/// prepared raw ADO statement (per <c>ef-core.md</c> §6): a
/// losing / retried claim resolves to one winner; the loser's
/// statement writes zero rows.
/// </summary>
public sealed class EfWorkInbox(WorkDbContext db, TimeProvider clock) : IWorkInbox
{
    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("inbox message id must not be empty", nameof(messageId));
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = WorkInboxSql.CreateClaimCommand(
                db.Database.GetDbConnection(),
                db.Database.CurrentTransaction?.GetDbTransaction(),
                messageId,
                clock.GetUtcNow());
            var rows = await command.ExecuteNonQueryAsync(cancellationToken);
            return rows == 1;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
