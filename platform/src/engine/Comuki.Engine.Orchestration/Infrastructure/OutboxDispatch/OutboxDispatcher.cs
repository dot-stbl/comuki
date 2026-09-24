using System.Data.Common;
using Comuki.Engine.Orchestration.Domain.Outbox;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Engine.Orchestration.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Orchestration.Infrastructure.OutboxDispatch;

/// <summary>
/// Durable dispatch sweep for <see cref="OutboxMessage"/>: one
/// <c>SELECT ... FOR UPDATE SKIP LOCKED</c> claim, then per-row publish
/// inside the same transaction. Row-lock is the only synchronisation
/// primitive — there is no separate lease timestamp on
/// <c>outbox_messages</c>, and a crash mid-sweep just rolls back the
/// transaction (the row is unlocked and available to the next sweep).
/// No explicit release step exists; <c>mark dispatched</c> /
/// <c>record failure</c> / <c>dead-letter</c> is the only path off the
/// claim.
/// </summary>
/// <param name="db">EF context — the dispatcher transaction shares its connection.</param>
/// <param name="publisher">Delivery transport; exceptions count as failures.</param>
/// <param name="clock"></param>
/// <param name="options">Bound from <c>Orchestration:Outbox</c>.</param>
public sealed class OutboxDispatcher(
    OrchestrationDbContext db,
    IOutboxPublisher publisher,
    TimeProvider clock,
    IOptions<OutboxOptions> options)
{
    /// <summary>
    /// Runs one dispatch sweep: claims up to <see cref="OutboxOptions.BatchSize"/>
    /// undispatched rows, publishes each, returns counts.
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task<(int Dispatched, int DeadLettered)> DispatchAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var batchSize = options.Value.BatchSize;
        var maxAttempts = options.Value.MaxAttempts;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var ids = await OutboxDispatchSql.ReadClaimedIdsAsync(
            OutboxDispatchSql.CreateClaimCommand(transaction.GetDbTransaction(), batchSize),
            cancellationToken);

        var dispatchedCount = 0;
        var deadLetteredCount = 0;

        foreach (var id in ids)
        {
            var message = await db.Set<OutboxMessage>().SingleAsync(m => m.Id == id, cancellationToken);
            try
            {
                await publisher.PublishAsync(message.Type, message.Payload, cancellationToken);
                message.MarkDispatched(now);
                dispatchedCount++;
            }
            // One poisoned message must not abort the whole batch — isolate
            // the failure on this row and let the rest dispatch.
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.RecordFailure(exception.Message, now, maxAttempts);
                if (message.IsDeadLettered)
                {
                    deadLetteredCount++;
                }
            }
        }

        if (ids.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (dispatchedCount, deadLetteredCount);
    }
}

/// <summary>
/// Guarded raw SQL + ADO plumbing for <see cref="OutboxDispatcher"/>.
/// The single statement claims undispatched, non-dead-lettered rows in
/// commit order via <c>FOR UPDATE SKIP LOCKED</c> — concurrent dispatchers
/// each take a disjoint slice, and the row-lock holds for the lifetime of
/// the dispatcher's transaction (so the subsequent EF read sees the
/// already-claimed row without a second locking clause).
/// </summary>
file static class OutboxDispatchSql
{
    /// <summary>Claim up to <c>@batchSize</c> undispatched rows, oldest first.</summary>
    public const string ClaimSql =
        "SELECT id FROM " + OrchestrationDatabase.Schema + "." + OrchestrationDatabase.OutboxMessages + " "
        + "WHERE dispatched_at IS NULL AND dead_lettered_at IS NULL "
        + "ORDER BY created_at "
        + "LIMIT @batchSize "
        + "FOR UPDATE SKIP LOCKED";

    /// <summary>Builds the prepared claim command on the transaction's connection.</summary>
    public static DbCommand CreateClaimCommand(DbTransaction transaction, int batchSize)
    {
        // boundary: ADO contract — Connection is always set on a live transaction
        var command = transaction.Connection!.CreateCommand();
        command.CommandText = ClaimSql;
        AddParameter(command, "@batchSize", batchSize);
        return command;
    }

    /// <summary>Reads every <c>id</c> (uuid) from the claim statement into a list.</summary>
    public static async Task<List<Guid>> ReadClaimedIdsAsync(DbCommand command, CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        await using (command)
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        return ids;
    }

    /// <summary>Adds one typed parameter (Npgsql infers uuid/timestamptz/text/int from the CLR value).</summary>
    public static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
