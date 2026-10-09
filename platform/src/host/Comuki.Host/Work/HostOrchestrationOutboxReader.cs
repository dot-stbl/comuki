using Comuki.Engine.Orchestration.Domain.Outbox;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Work.Application.Ports.Engine;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Work;

/// <summary>
/// Host-side adapter that closes <see cref="IOrchestrationOutboxReader"/>
/// over the engine's <c>OrchestrationDbContext</c>. The Work
/// module never references the engine's EF types — this adapter
/// is the only place that translates an EF
/// <see cref="OutboxMessage"/> row into the Work-side
/// <see cref="OrchestrationOutboxRow"/> projection. The SQL
/// filter (<c>type IN (...) AND id &gt; lastSeenId</c>) and the
/// <c>order by id asc take N</c> shape are unchanged from the
/// original in-process implementation; per-cycle scope and
/// per-row projection are the same contract the
/// <see cref="OrchestrationOutboxBatch"/> returns.
/// </summary>
public sealed class HostOrchestrationOutboxReader(OrchestrationDbContext db) : IOrchestrationOutboxReader
{
    private const int BatchLimit = 200;

    /// <inheritdoc />
    public async Task<OrchestrationOutboxBatch> PollAsync(
        IReadOnlyCollection<string> types,
        Guid lastSeenId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.OutboxMessages
            .AsNoTracking()
            .Where(message => types.Contains(message.Type) && message.Id > lastSeenId)
            .OrderBy(message => message.Id)
            .Take(BatchLimit)
            .Select(message => new OrchestrationOutboxRow(message.Id, message.Type, message.Payload))
            .ToListAsync(cancellationToken);

        var highest = rows.Count == 0 ? lastSeenId : rows[^1].Id;

        return new OrchestrationOutboxBatch(Read: rows.Count, HighestSeenId: highest, Rows: rows);
    }
}
