using System.Data.Common;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Comuki.Engine.Orchestration.Infrastructure.Inbox;

/// <summary>
/// EF implementation of <see cref="IInbox"/>: a guarded
/// <c>INSERT ... ON CONFLICT (message_id) DO NOTHING</c> against
/// <see cref="OrchestrationDatabase.InboxReceipts"/>. Returns
/// <c>rows == 1</c> exactly when this caller is the first to claim the
/// id; a concurrent double-claim resolves to exactly one winner via
/// the PK uniqueness constraint.
/// </summary>
/// <param name="db">Orchestration context of the current scope — the same connection (and, when the caller already opened one, the same ambient transaction) this claim enlists in.</param>
/// <param name="clock">Time source for the claimed receipt's <see cref="Domain.Inbox.InboxReceipt.ReceivedAt"/> stamp.</param>
internal sealed class InboxEf(OrchestrationDbContext db, TimeProvider clock) : IInbox
{
    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(string messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("inbox receipt message id must not be empty", nameof(messageId));
        }

        // Raw ADO command (ef-core.md §6's one legitimate exception: ON
        // CONFLICT has no ExecuteUpdate/ExecuteDelete equivalent), built the
        // house way (WorkItemQueueSql / OutboxDispatchSql) instead of EF's
        // ExecuteSqlRawAsync. OpenConnectionAsync/CloseConnectionAsync is
        // EF's own ref-counted pattern for mixing raw ADO with the context —
        // safe whether or not the caller already holds this connection open
        // inside its own BeginTransactionAsync (WS9's IntakeRunLauncher
        // does; a bare TryClaimAsync call with no ambient transaction, like
        // the InboxDedupeShould tests, doesn't).
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = InboxSql.CreateClaimCommand(
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

/// <summary>Guarded raw SQL + ADO plumbing for <see cref="InboxEf"/>.</summary>
file static class InboxSql
{
    /// <summary>Claim: insert the message id, ignore the conflict when already seen.</summary>
    public const string ClaimSql =
        "INSERT INTO " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.InboxReceipts + " "
        + "(message_id, received_at) VALUES (@messageId, @receivedAt) ON CONFLICT (message_id) DO NOTHING";

    /// <summary>Builds the prepared claim command on the connection (and ambient transaction, when the caller has one).</summary>
    public static DbCommand CreateClaimCommand(DbConnection connection, DbTransaction? transaction, string messageId, DateTimeOffset receivedAt)
    {
        var command = connection.CreateCommand();
        command.CommandText = ClaimSql;
        command.Transaction = transaction;
        AddParameter(command, "@messageId", messageId);
        AddParameter(command, "@receivedAt", receivedAt);
        return command;
    }

    /// <summary>Adds one typed parameter (Npgsql infers uuid/timestamptz/text from the CLR value).</summary>
    public static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
