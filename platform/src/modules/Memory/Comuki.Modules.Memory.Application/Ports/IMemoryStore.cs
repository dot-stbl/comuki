using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Ids;

namespace Comuki.Modules.Memory.Application.Ports;

/// <summary>
/// Read/write port over the long-term memory facts. The only write path is
/// <see cref="WriteAsync"/> (the memory.write tool / a human «запомни»);
/// same-topic writes supersede instead of deleting (audit history stays).
/// </summary>
public interface IMemoryStore
{
    /// <summary>
    /// Writes one fact, transactionally superseding the previous active row
    /// with the same (scope, subject, topic key) and storing the embedding
    /// when supplied.
    /// </summary>
    /// <param name="write"></param>
    /// <param name="cancellationToken"></param>
    public Task<MemoryFactView> WriteAsync(MemoryFactWrite write, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches visible facts: cosine-ranked when a query embedding is
    /// supplied and pgvector is available, otherwise the
    /// scope+kind+freshest fallback ranking.
    /// </summary>
    /// <param name="query"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<MemoryFactView>> SearchAsync(MemoryFactQuery query, CancellationToken cancellationToken = default);

    /// <summary>Lists every visible fact of one subject in fallback order (the /memory list surface).</summary>
    /// <param name="scope"></param>
    /// <param name="subjectId"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<MemoryFactView>> ListAsync(MemoryScope scope, string subjectId, CancellationToken cancellationToken = default);

    /// <summary>Forgets one fact by id (the /forget tool); true when a row was deleted.</summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    public Task<bool> ForgetAsync(MemoryFactId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes ephemeral facts past their TTL; returns how many rows went.
    /// Called by the sweep worker and available to tests.
    /// </summary>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>
    public Task<int> SweepExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
