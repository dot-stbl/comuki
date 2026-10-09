using Comuki.Modules.Work.Domain.Ids;

namespace Comuki.Modules.Work.Application.Ports.Persistence;

/// <summary>
/// Port for WorkTask aggregate reads / writes. The Work module owns
/// the aggregate shape; the Infrastructure project supplies the EF
/// implementation against the <c>__comuki_work</c> schema
/// (<c>WorkTask</c> + <c>WorkTaskAttempt</c> + side tables in task
/// 3.2 of the change). The Application layer only sees this
/// interface; the aggregate itself is a domain type. An in-memory
/// implementation lives in <c>Comuki.Modules.Work.Unit</c> for the
/// Application-handler unit tests.
/// </summary>
public interface IWorkTaskStore
{
    /// <summary>Loads the Task with the supplied id; null when not found.</summary>
    public Task<Domain.WorkTask?> FindAsync(WorkTaskId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the supplied aggregate. The implementation MUST
    /// write the row in the same transaction as the caller's
    /// outbox enqueue so the outbox row and the aggregate change
    /// commit atomically (per design decision 2 — Work ↔
    /// Orchestration via durable outbox/inbox, never dual
    /// writes). The caller's <c>SaveChangesAsync</c> is the
    /// commit; the implementation does not commit on its own.
    /// </summary>
    public Task SaveAsync(Domain.WorkTask task, CancellationToken cancellationToken = default);
}
