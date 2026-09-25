using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage;

namespace Comuki.Engine.Orchestration.Infrastructure.Queue;

/// <summary>
/// Run-status progression driven by work-item transitions: activation on the
/// first claim, finalization when the last item lands terminal, and
/// dependent-unblocking on a successful completion. All three run as guarded
/// <c>UPDATE ... RETURNING</c> statements inside the caller's item
/// transaction — status guards make them no-ops under concurrency, and a
/// returned row journals a <c>run.status_changed</c> event.
/// </summary>
internal static class RunProgression
{
    public static async Task ActivateAsync(
        OrchestrationDbContext db,
        IDbContextTransaction transaction,
        RunId runId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = WorkItemQueueSql.CreateRunActivationCommand(transaction.GetDbTransaction(), runId, now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            db.RunEvents.Add(RunEvent.Create(
                runId,
                RunEventTypes.RunStatusChanged,
                RunStatusPayload(nameof(RunStatus.Queued), reader.GetString(0), "worker"),
                now));
        }
    }

    /// <summary>Unblocks every Blocked dependent of <paramref name="workItemId"/>
    /// whose full prerequisite set has now reached Succeeded, in the caller's
    /// transaction — this is what makes a Blocked item Queued in the first
    /// place (the claim path itself never re-checks readiness; it only ever
    /// matches Queued). Only ever called after a successful completion, see
    /// the call site in WorkItemQueueEf.cs's WorkItemOwnedTransition.ApplyAsync.
    /// Locks candidate rows in ascending-id order first — see
    /// LockBlockedDependentsSql remarks in WorkItemQueueSql.cs for why.</summary>
    public static async Task UnblockDependentsAsync(
        IDbContextTransaction transaction,
        Guid workItemId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var lockCommand = WorkItemQueueSql.CreateLockBlockedDependentsCommand(transaction.GetDbTransaction(), workItemId))
        await using (var lockReader = await lockCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await lockReader.ReadAsync(cancellationToken))
            {
                // draining the reader is what acquires each candidate row's lock
            }
        }

        await using var command = WorkItemQueueSql.CreateUnblockDependentsCommand(transaction.GetDbTransaction(), workItemId, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task FinalizeAsync(
        OrchestrationDbContext db,
        IDbContextTransaction transaction,
        RunId runId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Serialize concurrent finalization attempts on this run row before
        // evaluating the NOT EXISTS guard — see LockRunForFinalizationSql
        // remarks in WorkItemQueueSql.cs.
        await using (var lockCommand = WorkItemQueueSql.CreateLockRunForFinalizationCommand(transaction.GetDbTransaction(), runId))
        {
            await lockCommand.ExecuteScalarAsync(cancellationToken);
        }

        await using var command = WorkItemQueueSql.CreateRunFinalizationCommand(transaction.GetDbTransaction(), runId, now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            db.RunEvents.Add(RunEvent.Create(
                runId,
                RunEventTypes.RunStatusChanged,
                RunStatusPayload(nameof(RunStatus.Running), reader.GetString(0), "worker"),
                now));
        }
    }

    /// <summary>Journal payload shape of a run transition — the same camelCase
    /// record the host adapters journal (from/to/actor).</summary>
    private static string RunStatusPayload(string from, string to, string actor)
    {
        return JsonSerializer.Serialize(
            new { from, to, actor }, JsonSerializerOptions.Web);
    }
}
