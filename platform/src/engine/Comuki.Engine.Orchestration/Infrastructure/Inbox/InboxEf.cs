using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Engine.Orchestration.Infrastructure.Inbox;

/// <summary>
/// EF implementation of <see cref="IInbox"/>: a guarded
/// <c>INSERT ... ON CONFLICT (message_id) DO NOTHING</c> against
/// <see cref="OrchestrationDatabase.InboxReceipts"/>. Returns
/// <c>rows == 1</c> exactly when this caller is the first to claim the
/// id; a concurrent double-claim resolves to exactly one winner via
/// the PK uniqueness constraint.
/// </summary>
/// <param name="db"></param>
/// <param name="clock"></param>
internal sealed class InboxEf(OrchestrationDbContext db, TimeProvider clock) : IInbox
{
    private const string Sql =
        "INSERT INTO " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.InboxReceipts + " "
        + "(message_id, received_at) VALUES ({0}, {1}) ON CONFLICT (message_id) DO NOTHING";

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(string messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("inbox receipt message id must not be empty", nameof(messageId));
        }

        var rows = await db.Database.ExecuteSqlRawAsync(
            Sql,
            [messageId, clock.GetUtcNow()],
            cancellationToken);

        return rows == 1;
    }
}
