using System.Data.Common;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Infrastructure.Queue;

/// <summary>
/// Guarded raw SQL + ADO plumbing for <see cref="WorkItemQueueEf"/>. Status
/// literals are the PascalCase enum names EF's <c>HasConversion&lt;string&gt;</c>
/// stores. Every mutation is guarded by lease owner (and live status) so
/// races between a slow worker and the reaper resolve safely in the store.
/// All SQL references the per-module <see cref="OrchestrationDatabase.Schema"/>
/// so the queries find the table regardless of <c>search_path</c>.
/// </summary>
internal static class WorkItemQueueSql
{
    /// <summary>Claim: oldest queued item matching the labels, row-locked for the update.</summary>
    public const string ClaimSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET status = 'Running', leased_by = @workerId, lease_until = @leaseUntil, "
        + "    heartbeat_at = @now, attempt = attempt + 1, updated_at = @now "
        + "WHERE id IN ( "
        + "    SELECT id FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "    WHERE status = 'Queued' "
        + "      AND profile_key = @profileKey "
        + "      AND image = @image "
        + "      AND profiles_ref = @profilesRef "
        + "    ORDER BY created_at "
        + "    LIMIT 1 "
        + "    FOR UPDATE SKIP LOCKED "
        + ") "
        + "RETURNING id, run_id, profile_key, brief, lease_until, attempt";

    /// <summary>Heartbeat: extend the lease, guarded by owner, running status and an unexpired lease.</summary>
    public const string HeartbeatSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET lease_until = @leaseUntil, heartbeat_at = @now, updated_at = @now "
        + "WHERE id = @workItemId AND leased_by = @workerId "
        + "  AND status = 'Running' AND lease_until > @now";

    /// <summary>Complete: running item owned by the worker -> succeeded, lease cleared.</summary>
    public const string CompleteSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET status = 'Succeeded', leased_by = NULL, lease_until = NULL, heartbeat_at = NULL, updated_at = @now "
        + "WHERE id = @workItemId AND leased_by = @workerId AND status = 'Running' "
        + "RETURNING run_id";

    /// <summary>Fail: running item owned by the worker -> failed, lease cleared.</summary>
    public const string FailSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET status = 'Failed', leased_by = NULL, lease_until = NULL, heartbeat_at = NULL, updated_at = @now "
        + "WHERE id = @workItemId AND leased_by = @workerId AND status = 'Running' "
        + "RETURNING run_id";

    /// <summary>Reap requeue: expired running lease with retries left -> back to queued.</summary>
    public const string ReapRequeueSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET status = 'Queued', leased_by = NULL, lease_until = NULL, heartbeat_at = NULL, updated_at = @now "
        + "WHERE status = 'Running' AND lease_until IS NOT NULL AND lease_until <= @cutoff AND attempt < @maxAttempts "
        + "RETURNING id, run_id, attempt";

    /// <summary>Reap fail: expired running lease out of retries -> failed.</summary>
    public const string ReapFailSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET status = 'Failed', leased_by = NULL, lease_until = NULL, heartbeat_at = NULL, updated_at = @now "
        + "WHERE status = 'Running' AND lease_until IS NOT NULL AND lease_until <= @cutoff AND attempt >= @maxAttempts "
        + "RETURNING id, run_id, attempt";

    /// <summary>Creates a prepared claim command on the transaction's connection.</summary>
    /// <param name="transaction"></param>
    /// <param name="workerId"></param>
    /// <param name="labels"></param>
    /// <param name="leaseUntil"></param>
    /// <param name="now"></param>
    public static DbCommand CreateClaimCommand(
        DbTransaction transaction,
        WorkerId workerId,
        WorkItemLabels labels,
        DateTimeOffset leaseUntil,
        DateTimeOffset now)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = ClaimSql;
        AddParameter(command, "@workerId", workerId.Value);
        AddParameter(command, "@profileKey", labels.ProfileKey);
        AddParameter(command, "@image", labels.Image);
        AddParameter(command, "@profilesRef", labels.ProfilesRef);
        AddParameter(command, "@leaseUntil", leaseUntil);
        AddParameter(command, "@now", now);
        return command;
    }

    /// <summary>Creates a prepared heartbeat command on the transaction's connection.</summary>
    /// <param name="transaction"></param>
    /// <param name="workItemId"></param>
    /// <param name="workerId"></param>
    /// <param name="leaseUntil"></param>
    /// <param name="now"></param>
    public static DbCommand CreateHeartbeatCommand(
        DbTransaction transaction,
        Guid workItemId,
        WorkerId workerId,
        DateTimeOffset leaseUntil,
        DateTimeOffset now)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = HeartbeatSql;
        AddParameter(command, "@workItemId", workItemId);
        AddParameter(command, "@workerId", workerId.Value);
        AddParameter(command, "@leaseUntil", leaseUntil);
        AddParameter(command, "@now", now);
        return command;
    }

    /// <summary>Creates a prepared complete command on the transaction's connection.</summary>
    /// <param name="transaction"></param>
    /// <param name="workItemId"></param>
    /// <param name="workerId"></param>
    /// <param name="now"></param>
    public static DbCommand CreateCompleteCommand(DbTransaction transaction, Guid workItemId, WorkerId workerId, DateTimeOffset now)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = CompleteSql;
        AddParameter(command, "@workItemId", workItemId);
        AddParameter(command, "@workerId", workerId.Value);
        AddParameter(command, "@now", now);
        return command;
    }

    /// <summary>Creates a prepared fail command on the transaction's connection.</summary>
    /// <param name="transaction"></param>
    /// <param name="workItemId"></param>
    /// <param name="workerId"></param>
    /// <param name="now"></param>
    public static DbCommand CreateFailCommand(DbTransaction transaction, Guid workItemId, WorkerId workerId, DateTimeOffset now)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = FailSql;
        AddParameter(command, "@workItemId", workItemId);
        AddParameter(command, "@workerId", workerId.Value);
        AddParameter(command, "@now", now);
        return command;
    }

    /// <summary>Creates a prepared reap-requeue command on the transaction's connection.</summary>
    /// <param name="transaction"></param>
    /// <param name="cutoff"></param>
    /// <param name="maxAttempts"></param>
    /// <param name="now"></param>
    public static DbCommand CreateReapRequeueCommand(DbTransaction transaction, DateTimeOffset cutoff, int maxAttempts, DateTimeOffset now)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = ReapRequeueSql;
        AddParameter(command, "@cutoff", cutoff);
        AddParameter(command, "@maxAttempts", maxAttempts);
        AddParameter(command, "@now", now);
        return command;
    }

    /// <summary>Creates a prepared reap-fail command on the transaction's connection.</summary>
    /// <param name="transaction"></param>
    /// <param name="cutoff"></param>
    /// <param name="maxAttempts"></param>
    /// <param name="now"></param>
    public static DbCommand CreateReapFailCommand(DbTransaction transaction, DateTimeOffset cutoff, int maxAttempts, DateTimeOffset now)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = ReapFailSql;
        AddParameter(command, "@cutoff", cutoff);
        AddParameter(command, "@maxAttempts", maxAttempts);
        AddParameter(command, "@now", now);
        return command;
    }

    /// <summary>Materialises the single <c>RETURNING</c> row of a claim into the contract DTO.</summary>
    /// <param name="reader"></param>
    public static ClaimedWorkItem ReadClaimed(DbDataReader reader)
    {
        return new ClaimedWorkItem(
            reader.GetGuid(0),
            new RunId(reader.GetGuid(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetInt32(5));
    }

    /// <summary>Adds one typed parameter (Npgsql infers uuid/timestamptz/text from the CLR value).</summary>
    /// <param name="command"></param>
    /// <param name="name"></param>
    /// <param name="value"></param>
    public static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}