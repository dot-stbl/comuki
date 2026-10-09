using Comuki.Modules.Work.Application.Ports.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Work.Infrastructure.Subscribers;

/// <summary>
/// Per-cycle outbox poll for the Work-side subscribers. The
/// dispatcher is the seam that turns the engine outbox into a
/// type-filtered, watermark-paged stream — the actual
/// <c>OrchestrationDbContext</c> read lives behind
/// <see cref="IOrchestrationOutboxReader"/> (host-adapter), so the
/// Work module never references the engine's EF types. Each
/// subscriber consumes the
/// <see cref="OrchestrationOutboxRow"/> projection; the dispatcher
/// is the point that opens the per-cycle scope and resolves the
/// reader from DI.
/// </summary>
public sealed class WorkOutboxPollDispatcher(IServiceScopeFactory scopeFactory)
{
    /// <summary>
    /// Per-cycle outbox poll with per-row payload — the
    /// dispatch-side seam. Returns the up-to-<c>200</c> rows
    /// whose <c>type</c> is in <paramref name="types"/> AND whose
    /// <c>id &gt; lastSeenId</c>, plus the highest <c>id</c>
    /// observed so the subscriber can advance its watermark. The
    /// <c>HighestSeenId</c> is the SQL-ordered tail's id; the
    /// reader keeps the .NET-<see cref="Guid"/>-vs-PG-<c>uuid</c>
    ///  ordering out of the consumer's hands.
    /// </summary>
    public async Task<OrchestrationOutboxBatch> PollDispatchAsync(
        IReadOnlyCollection<string> types,
        Guid lastSeenId,
        CancellationToken cancellationToken = default)
    {
        if (types.Count == 0)
        {
            return new OrchestrationOutboxBatch(Read: 0, HighestSeenId: lastSeenId, Rows: []);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IOrchestrationOutboxReader>();
        return await reader.PollAsync(types, lastSeenId, cancellationToken);
    }
}
