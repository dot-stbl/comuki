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

    /// <summary>Lists visible facts of one subject in fallback order (the /memory list surface).</summary>
    /// <param name="scope"></param>
    /// <param name="subjectId"></param>
    /// <param name="limit">Maximum facts returned. Default <see cref="DefaultListLimit"/>.</param>
    /// <param name="offset">Number of facts to skip. Default <c>0</c>.</param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<MemoryFactView>> ListAsync(
        MemoryScope scope,
        string subjectId,
        int limit = DefaultListLimit,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>Default page size for <see cref="ListAsync"/>; mirrors the search-side <c>Limit</c> default.</summary>
    public const int DefaultListLimit = 100;

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

    /// <summary>
    /// Promotes proven-useful ephemeral facts to standing: active rows
    /// read at least <paramref name="readThreshold"/> times and older than
    /// <paramref name="minAge"/>. Returns how many rows were promoted.
    /// Called by the consolidation worker.
    /// </summary>
    /// <param name="now"></param>
    /// <param name="readThreshold">Reads needed for promotion.</param>
    /// <param name="minAge">Minimum age before a fact is eligible (a fresh write's digest reads must not promote it instantly).</param>
    /// <param name="cancellationToken"></param>
    public Task<int> PromoteReadFactsAsync(DateTimeOffset now, int readThreshold, TimeSpan minAge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decays forgotten standing facts to ephemeral: active rows whose
    /// last read (or creation, when never read) is older than
    /// <paramref name="unreadWindow"/>. The decayed rows get a backdated
    /// <c>created_at</c> leaving <see cref="Domain.Facts.MemoryFactPolicy.DecayRemainingTtl"/>
    /// of grace before the sweep reaps them. Returns how many rows decayed.
    /// Called by the consolidation worker.
    /// </summary>
    /// <param name="now"></param>
    /// <param name="unreadWindow">How long unread before decaying.</param>
    /// <param name="cancellationToken"></param>
    public Task<int> DecayUnreadFactsAsync(DateTimeOffset now, TimeSpan unreadWindow, CancellationToken cancellationToken = default);

    /// <summary>Counts active (not superseded) facts of every scope — the consolidation worker's totals.</summary>
    /// <param name="cancellationToken"></param>
    public Task<int> CountActiveFactsAsync(CancellationToken cancellationToken = default);
}
