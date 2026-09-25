using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Outbox;
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
        IOutbox outbox,
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
            var status = reader.GetString(0);
            var projectId = new ProjectId(reader.GetGuid(1));

            db.RunEvents.Add(RunEvent.Create(
                runId,
                RunEventTypes.RunStatusChanged,
                RunStatusPayload(nameof(RunStatus.Running), status, "worker"),
                now));

            // WS7 (issue #87): this block only runs when THIS call performed the
            // guarded finalizing UPDATE (a non-empty RETURNING) — a losing
            // concurrent finalize attempt (see LockRunForFinalizationSql remarks
            // above) never reaches here, so exactly one outbox row is enqueued
            // per Run termination even under the WS2 concurrent-finalize races.
            outbox.Enqueue(RunEventTypes.RunTerminatedV1, RunTerminatedPayload(runId, projectId, status, now));
        }
    }

    /// <summary>Journal payload shape of a run transition — the same camelCase
    /// record the host adapters journal (from/to/actor).</summary>
    private static string RunStatusPayload(string from, string to, string actor)
    {
        return JsonSerializer.Serialize(
            new { from, to, actor }, JsonSerializerOptions.Web);
    }

    /// <summary>Outbox contract payload for <see cref="RunEventTypes.RunTerminatedV1"/> —
    /// runId/projectId/status/occurredAt. Deliberately internal (not private) so
    /// RunTerminatedOutboxPayloadShould (WS7, issue #87), in the StatusMachine unit
    /// test project, can assert its exact JSON shape without a database — this
    /// project already grants that project InternalsVisibleTo.</summary>
    internal static string RunTerminatedPayload(RunId runId, ProjectId projectId, string status, DateTimeOffset occurredAt)
    {
        return JsonSerializer.Serialize(
            new { runId = runId.Value, projectId = projectId.Value, status, occurredAt },
            JsonSerializerOptions.Web);
    }
}
