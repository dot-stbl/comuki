using System.Data.Common;
using Comuki.Engine.Orchestration.Domain.MergeQueue;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence.Stores;

/// <summary>
/// Guarded raw SQL for the merge-queue claim + helpers. PascalCase
/// status literals mirror <c>HasConversion&lt;string&gt;</c> — sourced from
/// <see cref="MergeQueueStatus"/> via <c>nameof</c> so a rename fails the
/// build instead of leaving these predicates silently stale. Every
/// query is parameterised; everything routes through the
/// per-module schema constant.
/// </summary>
internal static class MergeQueueStoreSql
{
    /// <summary>Compiler-checked status name — see the class remarks.</summary>
    private const string Pending = nameof(MergeQueueStatus.Pending);

    /// <summary>Compiler-checked status name — see the class remarks.</summary>
    private const string InProgress = nameof(MergeQueueStatus.InProgress);

    /// <summary>Atomically claim the oldest pending row in scope; race-safe via FOR UPDATE SKIP LOCKED.</summary>
    public const string ClaimNextSql =
        "UPDATE " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.MergeQueue + " "
        + "SET status = '" + InProgress + "', claimed_by = @operatorId, claimed_at = @now "
        + "WHERE id IN ( "
        + "    SELECT id FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.MergeQueue + " "
        + "    WHERE status = '" + Pending + "' "
        + "      AND (@projectId IS NULL OR project_id = @projectId) "
        + "    ORDER BY enqueued_at "
        + "    LIMIT 1 "
        + "    FOR UPDATE SKIP LOCKED "
        + ") "
        + "RETURNING id, project_id, branch_name, pull_request_url, status, conflict_resolution, "
        + "          enqueued_at, claimed_by, claimed_at, merged_at, abandoned_at, abandoned_reason, notes";

    /// <summary>
    /// Builds the prepared claim command on the transaction's
    /// connection. Both nullable and non-nullable <paramref name="projectId"/>
    /// are handled by passing DBNull when the caller wants the
    /// cross-project scope.
    /// </summary>
    /// <param name="transaction"></param>
    /// <param name="projectId"></param>
    /// <param name="operatorId"></param>
    /// <param name="now"></param>
    public static DbCommand CreateClaimNextCommand(
        IDbContextTransaction transaction,
        ProjectId? projectId,
        string operatorId,
        DateTimeOffset now)
    {
        var command = transaction.GetDbTransaction().Connection!.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = ClaimNextSql;

        var projectIdParameter = new NpgsqlParameter("@projectId", System.Data.DbType.Guid)
        {
            Value = projectId is { } pid ? pid.Value : DBNull.Value
        };
        command.Parameters.Add(projectIdParameter);

        command.Parameters.Add(new NpgsqlParameter("@operatorId", System.Data.DbType.String) { Value = operatorId });
        command.Parameters.Add(new NpgsqlParameter("@now", System.Data.DbType.DateTimeOffset) { Value = now });

        return command;
    }

    /// <summary>Materialises one <c>RETURNING</c> row into an entry. Caller checks ReadAsync first.</summary>
    /// <param name="reader"></param>
    public static MergeQueueEntry ReadClaimed(DbDataReader reader)
    {
        var projectIdOrdinal = reader.GetOrdinal("project_id");
        var claimedByOrdinal = reader.GetOrdinal("claimed_by");
        var claimedAtOrdinal = reader.GetOrdinal("claimed_at");
        var mergedAtOrdinal = reader.GetOrdinal("merged_at");
        var abandonedAtOrdinal = reader.GetOrdinal("abandoned_at");
        var abandonedReasonOrdinal = reader.GetOrdinal("abandoned_reason");
        var notesOrdinal = reader.GetOrdinal("notes");

        return MergeQueueEntry.Reconstitute(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.IsDBNull(projectIdOrdinal)
                ? null
                : new ProjectId(reader.GetGuid(projectIdOrdinal)),
            reader.GetString(reader.GetOrdinal("branch_name")),
            reader.GetString(reader.GetOrdinal("pull_request_url")),
            Enum.Parse<MergeQueueStatus>(reader.GetString(reader.GetOrdinal("status"))),
            Enum.Parse<ConflictResolution>(reader.GetString(reader.GetOrdinal("conflict_resolution"))),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("enqueued_at")),
            reader.IsDBNull(claimedByOrdinal) ? null : reader.GetString(claimedByOrdinal),
            reader.IsDBNull(claimedAtOrdinal) ? null : reader.GetFieldValue<DateTimeOffset>(claimedAtOrdinal),
            reader.IsDBNull(mergedAtOrdinal) ? null : reader.GetFieldValue<DateTimeOffset>(mergedAtOrdinal),
            reader.IsDBNull(abandonedAtOrdinal) ? null : reader.GetFieldValue<DateTimeOffset>(abandonedAtOrdinal),
            reader.IsDBNull(abandonedReasonOrdinal) ? null : reader.GetString(abandonedReasonOrdinal),
            reader.IsDBNull(notesOrdinal) ? null : reader.GetString(notesOrdinal));
    }
}
