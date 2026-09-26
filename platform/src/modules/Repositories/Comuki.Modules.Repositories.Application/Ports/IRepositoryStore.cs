using Comuki.Modules.Repositories.Domain.Ids;
using Comuki.Modules.Repositories.Domain.Repositories;

namespace Comuki.Modules.Repositories.Application.Ports;

/// <summary>
/// Persistence port for the repository registry. Implemented by the
/// module infrastructure over its scoped <c>RepositoriesDbContext</c>;
/// Application code never touches EF directly.
/// <para>
/// <see cref="GetOrRegisterAsync"/> is the dedup entry point: when a
/// Repository with the same normalized (host, url) identity is already
/// registered, the existing row is returned untouched and the candidate
/// graph (Repository + RepositoryPolicy + RepositoryCredentialRef) is
/// discarded. Otherwise the candidate graph is persisted in one unit of
/// work and the new row is returned — the unique index
/// <c>ux_repositories_host_url</c> is the concurrency arbiter (a second
/// concurrent caller races into the index, not into a read-then-write
/// check; the loser sees a <c>DbUpdateException</c> and the store retries
/// the lookup).
/// </para>
/// </summary>
public interface IRepositoryStore
{
    /// <summary>Tracked find by id; null when no row exists.</summary>
    /// <param name="repositoryId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<Repository?> FindByIdAsync(RepositoryId repositoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find by the (host, url) dedup key — both arguments are normalized
    /// via <see cref="RepositoryIdentity.Of"/> before the query so
    /// casing/whitespace variants resolve to the same row.
    /// </summary>
    /// <param name="host"></param>
    /// <param name="url"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<Repository?> FindByIdentityAsync(string host, string url, CancellationToken cancellationToken = default);

    /// <summary>No-tracking list ordered by creation time (UUIDv7 monotonic).</summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<Repository>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent register: returns the EXISTING row when a Repository
    /// with the same (host, url) identity is already registered (spec
    /// dedup), otherwise persists the candidate graph (Repository +
    /// <paramref name="policy"/> + <paramref name="credentialRef"/>) in
    /// one unit of work and returns the new row.
    /// </summary>
    /// <param name="candidate">The Repository to register when no existing row matches.</param>
    /// <param name="policy">The policy snapshot to persist alongside the new row.</param>
    /// <param name="credentialRef">The credential reference to persist alongside the new row.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<Repository> GetOrRegisterAsync(
        Repository candidate,
        RepositoryPolicy policy,
        RepositoryCredentialRef credentialRef,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a tracked graph (used after <see cref="Repository.Update"/> on a previously loaded row).</summary>
    /// <param name="repository"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task SaveAsync(Repository repository, CancellationToken cancellationToken = default);
}
