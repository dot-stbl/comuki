using System.Data.Common;

namespace Comuki.Modules.Work.Infrastructure.Persistence.WatermarkSql;

/// <summary>
/// Raw ADO plumbing for <see cref="Stores.EfWorkInboxWatermarkStore"/>.
/// The <c>ON CONFLICT … DO UPDATE … GREATEST(excluded.last_seen_id,
/// work.outbox_watermarks.last_seen_id)</c> shape holds the
/// monotonic-direction contract on the SQL engine's side — the
/// smaller id is silently ignored by <c>GREATEST</c> so a
/// re-processing retry never moves the watermark backward.
/// Mirrors <c>engine/.../Infrastructure/Inbox/InboxSql.cs</c>'s
/// "raw ADO, prepared command, ambient transaction" pattern
/// (<c>ef-core.md</c> §6's one legitimate exception).
/// </summary>
public static class WatermarkStoreUpsert
{
    /// <summary>Upsert: <c>INSERT … ON CONFLICT (subscriber, type) DO UPDATE SET last_seen_id = GREATEST(...), updated_at = …</c>.</summary>
    public const string UpsertSql =
        "INSERT INTO " + WorkDatabase.Schema + "." + WorkDatabase.OutboxWatermarks + " "
        + "(subscriber, type, last_seen_id, updated_at) "
        + "VALUES (@subscriber, @type, @lastSeenId, @updatedAt) "
        + "ON CONFLICT (subscriber, type) DO UPDATE SET "
        + "last_seen_id = GREATEST(work.outbox_watermarks.last_seen_id, EXCLUDED.last_seen_id), "
        + "updated_at = EXCLUDED.updated_at";

    /// <summary>Builds the prepared upsert command on the connection (and ambient transaction, when the caller has one).</summary>
    public static DbCommand CreateUpsertCommand(
        DbConnection connection,
        DbTransaction? transaction,
        string subscriber,
        string type,
        Guid lastSeenId,
        DateTimeOffset updatedAt)
    {
        var command = connection.CreateCommand();
        command.CommandText = UpsertSql;
        command.Transaction = transaction;
        AddParameter(command, "@subscriber", subscriber);
        AddParameter(command, "@type", type);
        AddParameter(command, "@lastSeenId", lastSeenId);
        AddParameter(command, "@updatedAt", updatedAt);
        return command;
    }

    /// <summary>Runs the prepared upsert on the supplied connection / ambient transaction.</summary>
    public static async Task UpsertAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string subscriber,
        string type,
        Guid lastSeenId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await using var command = CreateUpsertCommand(connection, transaction, subscriber, type, lastSeenId, updatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
