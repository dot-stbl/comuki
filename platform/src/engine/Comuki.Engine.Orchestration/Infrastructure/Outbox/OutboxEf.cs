using Comuki.Engine.Orchestration.Domain.Outbox;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;

namespace Comuki.Engine.Orchestration.Infrastructure.Outbox;

/// <summary>
/// EF implementation of <see cref="IOutbox"/>: stages an
/// <see cref="OutboxMessage"/> on the caller's
/// <see cref="OrchestrationDbContext"/>. The caller's own
/// <c>SaveChangesAsync</c> commits the row atomically with the aggregate
/// change it reports.
/// </summary>
/// <param name="db"></param>
/// <param name="clock"></param>
internal sealed class OutboxEf(OrchestrationDbContext db, TimeProvider clock) : IOutbox
{
    /// <inheritdoc />
    public void Enqueue(string type, string payloadJson)
    {
        db.Add(OutboxMessage.Create(type, payloadJson, clock.GetUtcNow()));
    }
}
