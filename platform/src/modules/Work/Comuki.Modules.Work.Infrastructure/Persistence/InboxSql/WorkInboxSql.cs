using System.Data.Common;

namespace Comuki.Modules.Work.Infrastructure.Persistence.InboxSql;

/// <summary>
/// Raw ADO plumbing for
/// <see cref="Stores.EfWorkInbox"/>. Mirrors the engine's
/// <c>InboxEf</c> + <c>InboxSql</c> split — the WS9 admission-claim
/// pattern, as it should be: <c>INSERT … ON CONFLICT (message_id)
/// DO NOTHING</c> against <c>work.inbox_receipts</c>. Returns
/// <c>rows == 1</c> on a winning claim, <c>0</c> on a duplicate.
/// Mixed raw ADO with EF (per <c>ef-core.md</c> §6's one
/// legitimate exception to the rule — ON CONFLICT has no
/// <c>ExecuteUpdate</c> / <c>ExecuteDelete</c> equivalent).
/// </summary>
public static class WorkInboxSql
{
    /// <summary>Claim: insert the message id, ignore the conflict when already seen.</summary>
    public const string ClaimSql =
        "INSERT INTO " + WorkDatabase.Schema + "." + WorkDatabase.InboxReceipts + " "
        + "(message_id, claimed_at) VALUES (@messageId, @claimedAt) "
        + "ON CONFLICT (message_id) DO NOTHING";

    /// <summary>Builds the prepared claim command on the connection (and ambient transaction, when the caller has one).</summary>
    public static DbCommand CreateClaimCommand(
        DbConnection connection,
        DbTransaction? transaction,
        string messageId,
        DateTimeOffset claimedAt)
    {
        var command = connection.CreateCommand();
        command.CommandText = ClaimSql;
        command.Transaction = transaction;
        AddParameter(command, "@messageId", messageId);
        AddParameter(command, "@claimedAt", claimedAt);
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
