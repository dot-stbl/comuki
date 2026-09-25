using System.Data.Common;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Infrastructure.Queue;

/// <summary>
/// Guarded raw SQL + ADO plumbing for <see cref="WorkItemQueueEf"/>. Status
/// literals are the PascalCase enum names EF's <c>HasConversion&lt;string&gt;</c>
/// stores — sourced from <see cref="WorkItemStatus"/> via <c>nameof</c> so a
/// rename of a status member fails the build instead of leaving these
/// predicates silently stale. Every mutation is guarded by lease owner (and
/// live status) so races between a slow worker and the reaper resolve safely
/// in the store. All SQL references the per-module
/// <see cref="OrchestrationDatabase.Schema"/> so the queries find the table
/// regardless of <c>search_path</c>.
/// </summary>
internal static class WorkItemQueueSql
{
    /// <summary>Compiler-checked status name — see the class remarks.</summary>
    private const string Queued = nameof(WorkItemStatus.Queued);

    /// <summary>Compiler-checked status name — see the class remarks.</summary>
    private const string Running = nameof(WorkItemStatus.Running);

    /// <summary>Compiler-checked status name — see the class remarks.</summary>
    private const string Succeeded = nameof(WorkItemStatus.Succeeded);

    /// <summary>Compiler-checked status name — see the class remarks.</summary>
    private const string Failed = nameof(WorkItemStatus.Failed);

    /// <summary>Compiler-checked status name — see the class remarks.</summary>
    private const string Blocked = nameof(WorkItemStatus.Blocked);

    /// <summary>Compiler-checked run status literals (nameof over the enum members).</summary>
    private const string RunWaitingInQueue = nameof(RunStatus.Queued);

    /// <summary>Compiler-checked run status literal.</summary>
    private const string RunActivated = nameof(RunStatus.Running);

    /// <summary>Compiler-checked run status literal.</summary>
    private const string RunSucceeded = nameof(RunStatus.Succeeded);

    /// <summary>Compiler-checked run status literal.</summary>
    private const string RunFailed = nameof(RunStatus.Failed);

    /// <summary>Claim: oldest queued item matching the labels, row-locked for the update.</summary>
    public const string ClaimSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET status = '" + Running + "', leased_by = @workerId, lease_until = @leaseUntil, "
        + "    heartbeat_at = @now, attempt = attempt + 1, updated_at = @now "
        + "WHERE id IN ( "
        + "    SELECT id FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "    WHERE status = '" + Queued + "' "
        + "      AND profile_key = @profileKey "
        + "      AND image = @image "
        + "      AND profiles_ref = @profilesRef "
        + "    ORDER BY created_at "
        + "    LIMIT 1 "
        + "    FOR UPDATE SKIP LOCKED "
        + ") "
        + "RETURNING id, run_id, "
        + "(SELECT r.project_id FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.Runs + " r WHERE r.id = work_items.run_id), "
        + "profile_key, brief, lease_until, attempt";

    /// <summary>Heartbeat: extend the lease, guarded by owner, running status and an unexpired lease.</summary>
    public const string HeartbeatSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET lease_until = @leaseUntil, heartbeat_at = @now, updated_at = @now "
        + "WHERE id = @workItemId AND leased_by = @workerId "
        + "  AND status = '" + Running + "' AND lease_until > @now";

    /// <summary>Complete: running item owned by the worker -> succeeded, lease cleared.</summary>
    public const string CompleteSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET status = '" + Succeeded + "', leased_by = NULL, lease_until = NULL, heartbeat_at = NULL, updated_at = @now "
        + "WHERE id = @workItemId AND leased_by = @workerId AND status = '" + Running + "' "
        + "RETURNING run_id";

    /// <summary>Fail: running item owned by the worker -> failed, lease cleared.</summary>
    public const string FailSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET status = '" + Failed + "', leased_by = NULL, lease_until = NULL, heartbeat_at = NULL, updated_at = @now "
        + "WHERE id = @workItemId AND leased_by = @workerId AND status = '" + Running + "' "
        + "RETURNING run_id";

    /// <summary>Unblock: every Blocked dependent of a just-succeeded item whose
    /// full prerequisite set has now reached Succeeded moves to Queued, in the
    /// same transaction that completed the prerequisite. Only ever selects
    /// dependents of <c>@workItemId</c>, so the caller runs it once per
    /// completion instead of a polling sweep over the whole table. Callers
    /// only invoke this after a successful completion — a Failed/Cancelled
    /// prerequisite must not auto-unblock its dependents (see
    /// WorkItemOwnedTransition.ApplyAsync in WorkItemQueueEf.cs).</summary>
    public const string UnblockDependentsSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET status = '" + Queued + "', updated_at = @now "
        + "WHERE status = '" + Blocked + "' "
        + "  AND id IN ( "
        + "      SELECT dependency.work_item_id "
        + "      FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItemDependencies + " dependency "
        + "      WHERE dependency.depends_on_work_item_id = @workItemId "
        + "  ) "
        + "  AND NOT EXISTS ( "
        + "      SELECT 1 "
        + "      FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItemDependencies + " remaining "
        + "      JOIN " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " prerequisite "
        + "        ON prerequisite.id = remaining.depends_on_work_item_id "
        + "      WHERE remaining.work_item_id = " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + ".id "
        + "        AND prerequisite.status <> '" + Succeeded + "' "
        + "  ) "
        + "RETURNING id";

    /// <summary>Reap requeue: expired running lease with retries left -> back to queued.</summary>
    public const string ReapRequeueSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET status = '" + Queued + "', leased_by = NULL, lease_until = NULL, heartbeat_at = NULL, updated_at = @now "
        + "WHERE status = '" + Running + "' AND lease_until IS NOT NULL AND lease_until <= @cutoff AND attempt < @maxAttempts "
        + "RETURNING id, run_id, attempt";

    /// <summary>Reap fail: expired running lease out of retries -> failed.</summary>
    public const string ReapFailSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "SET status = '" + Failed + "', leased_by = NULL, lease_until = NULL, heartbeat_at = NULL, updated_at = @now "
        + "WHERE status = '" + Running + "' AND lease_until IS NOT NULL AND lease_until <= @cutoff AND attempt >= @maxAttempts "
        + "RETURNING id, run_id, attempt";

    /// <summary>Run activation on the first claim of a run's items:
    /// <c>Queued -> Running</c>, guarded by the current status so concurrent
    /// claims fire it exactly once. Empty <c>RETURNING</c> = someone else
    /// already activated (or the run left Queued) — nothing to journal.</summary>
    public const string RunActivationSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.Runs + " "
        + "SET status = '" + RunActivated + "', updated_at = @now "
        + "WHERE id = @runId AND status = '" + RunWaitingInQueue + "' "
        + "RETURNING status";

    /// <summary>Run finalization after a terminal item transition: when every
    /// item of the run is terminal, <c>Running -> Succeeded</c> (no failed
    /// items) or <c>Running -> Failed</c> (any failed item). Only fires from
    /// <c>Running</c> — Waiting/Escalated runs belong to their own flows
    /// (the run transition table has no Waiting/Escalated -> Succeeded
    /// edge). Empty <c>RETURNING</c> = items remain or the run is elsewhere.</summary>
    public const string RunFinalizationSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.Runs + " "
        + "SET status = CASE WHEN EXISTS (SELECT 1 FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " wi "
        + "        WHERE wi.run_id = @runId AND wi.status = '" + Failed + "') "
        + "    THEN '" + RunFailed + "' ELSE '" + RunSucceeded + "' END, "
        + "    updated_at = @now "
        + "WHERE id = @runId AND status = '" + RunActivated + "' "
        + "  AND NOT EXISTS (SELECT 1 FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " wi "
        + "        WHERE wi.run_id = @runId AND wi.status IN ('" + Blocked + "', '" + Queued + "', '" + Running + "')) "
        + "RETURNING status, project_id";

    /// <summary>Locks the run row before the finalization guard evaluates
    /// <c>work_items</c> state via NOT EXISTS. Postgres only auto-serializes
    /// concurrent UPDATEs that examine the TARGET row's own column (see
    /// RunActivationSql's simple status guard) — a NOT EXISTS subquery
    /// against a different table is not a conflict target for the runs row,
    /// so without this explicit lock two transactions finalizing a run's
    /// last two work items concurrently can each see the other's
    /// not-yet-committed terminal write as still open and BOTH skip
    /// finalization. Locking first forces the second transaction to wait
    /// for the first to commit, so its next statement (a fresh snapshot)
    /// sees the up-to-date work_items state.</summary>
    public const string LockRunForFinalizationSql =
        "SELECT id FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.Runs + " "
        + "WHERE id = @runId FOR UPDATE";

    /// <summary>Locks this completing item's Blocked dependent candidates, in
    /// deterministic ascending-id order, before the guarded unblock UPDATE
    /// below. Two prerequisites of a shared (diamond) dependent completing
    /// concurrently each run a multi-row UPDATE whose candidate sets can
    /// overlap on the same dependent rows; without a canonical lock order
    /// first, Postgres may lock those overlapping rows in planner-dependent
    /// (not necessarily matching) order across the two transactions — a
    /// classic multi-row-update deadlock (40P01). Locking the same
    /// candidate ids in the same ascending-id order up front makes every
    /// transaction acquire overlapping row locks in the same sequence, so
    /// at most one waits — it never cycles.</summary>
    public const string LockBlockedDependentsSql =
        "SELECT id FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItems + " "
        + "WHERE status = '" + Blocked + "' "
        + "  AND id IN ( "
        + "      SELECT dependency.work_item_id "
        + "      FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.WorkItemDependencies + " dependency "
        + "      WHERE dependency.depends_on_work_item_id = @workItemId "
        + "  ) "
        + "ORDER BY id "
        + "FOR UPDATE";

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

    /// <summary>Creates a prepared unblock-dependents command on the transaction's connection.</summary>
    /// <param name="transaction"></param>
    /// <param name="workItemId"></param>
    /// <param name="now"></param>
    public static DbCommand CreateUnblockDependentsCommand(DbTransaction transaction, Guid workItemId, DateTimeOffset now)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = UnblockDependentsSql;
        AddParameter(command, "@workItemId", workItemId);
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

    /// <summary>Creates a prepared run-activation command on the transaction's connection.</summary>
    /// <param name="transaction"></param>
    /// <param name="runId"></param>
    /// <param name="now"></param>
    public static DbCommand CreateRunActivationCommand(DbTransaction transaction, RunId runId, DateTimeOffset now)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = RunActivationSql;
        AddParameter(command, "@runId", runId.Value);
        AddParameter(command, "@now", now);
        return command;
    }

    /// <summary>Creates a prepared run-finalization command on the transaction's connection.</summary>
    /// <param name="transaction"></param>
    /// <param name="runId"></param>
    /// <param name="now"></param>
    public static DbCommand CreateRunFinalizationCommand(DbTransaction transaction, RunId runId, DateTimeOffset now)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = RunFinalizationSql;
        AddParameter(command, "@runId", runId.Value);
        AddParameter(command, "@now", now);
        return command;
    }

    /// <summary>Creates a prepared run-lock command (run before
    /// <see cref="CreateRunFinalizationCommand"/> in the same transaction —
    /// see <see cref="LockRunForFinalizationSql"/> remarks).</summary>
    public static DbCommand CreateLockRunForFinalizationCommand(DbTransaction transaction, RunId runId)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = LockRunForFinalizationSql;
        AddParameter(command, "@runId", runId.Value);
        return command;
    }

    /// <summary>Creates a prepared lock command for this completing item's
    /// Blocked dependent candidates — run before
    /// <see cref="CreateUnblockDependentsCommand"/> in the same transaction;
    /// see <see cref="LockBlockedDependentsSql"/> remarks.</summary>
    public static DbCommand CreateLockBlockedDependentsCommand(DbTransaction transaction, Guid workItemId)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = LockBlockedDependentsSql;
        AddParameter(command, "@workItemId", workItemId);
        return command;
    }

    /// <summary>Materialises the single <c>RETURNING</c> row of a claim into the contract DTO.</summary>
    /// <param name="reader"></param>
    public static ClaimedWorkItem ReadClaimed(DbDataReader reader)
    {
        return new ClaimedWorkItem(
            reader.GetGuid(0),
            new RunId(reader.GetGuid(1)),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetInt32(6));
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
