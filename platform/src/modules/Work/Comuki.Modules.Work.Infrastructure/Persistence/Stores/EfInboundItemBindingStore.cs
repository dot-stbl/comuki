using Comuki.Modules.Work.Application.Admission;
using Comuki.Modules.Work.Domain.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Work.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of the inbound-id → WorkTaskId binding
/// (per <see cref="IInboundItemBindingStore"/>). One row per
/// inbound id; uniqueness on the inbound external id is enforced
/// by a unique index that lands in the <c>WorkInit</c> migration
/// (the Integrations-admission hook's idempotency path queries by
/// <c>inbound_external_id</c> to find the original Task on
/// replay). The store lives in <c>work</c> because the binding
/// is a Work-side concept (Work is the one that owns the
/// inbound-id-to-Task map; Integrations just publishes the
/// <c>admitted.v1</c> event).
/// </summary>
public sealed class EfInboundItemBindingStore(WorkDbContext db, TimeProvider clock) : IInboundItemBindingStore
{
    /// <inheritdoc />
    public async Task<WorkTaskId?> FindByInboundAsync(string inboundItemExternalId, CancellationToken cancellationToken = default)
    {
        var entity = await db.Set<InboundItemBindingEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.InboundItemExternalId == inboundItemExternalId, cancellationToken);

        return entity is null ? null : new WorkTaskId(entity.TaskId);
    }

    /// <inheritdoc />
    public async Task BindAsync(string inboundItemExternalId, WorkTaskId taskId, CancellationToken cancellationToken = default)
    {
        db.Set<InboundItemBindingEntity>().Add(new InboundItemBindingEntity(
            Id: Guid.NewGuid(),
            InboundItemExternalId: inboundItemExternalId,
            TaskId: taskId.Value,
            BoundAt: clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>EF entity for <c>work.inbound_item_bindings</c> — the inbound external id → WorkTaskId map.</summary>
    public sealed record InboundItemBindingEntity(
        Guid Id,
        string InboundItemExternalId,
        Guid TaskId,
        DateTimeOffset BoundAt)
    {
        public InboundItemBindingEntity() : this(
            Id: Guid.Empty,
            InboundItemExternalId: string.Empty,
            TaskId: Guid.Empty,
            BoundAt: default)
        {
        }
    }
}
