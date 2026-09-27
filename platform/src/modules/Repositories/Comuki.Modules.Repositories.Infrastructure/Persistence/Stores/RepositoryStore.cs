using Comuki.Modules.Repositories.Application.Ports;
using Comuki.Modules.Repositories.Domain.Ids;
using Comuki.Modules.Repositories.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Repositories.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IRepositoryStore"/> over the scoped
/// <see cref="RepositoriesDbContext"/>. Finds track (the unit-of-work pattern:
/// load → mutate → save in one scope); the list read is no-tracking.
/// <para>
/// <see cref="GetOrRegisterAsync"/> queries by the normalized (host, url)
/// identity first; on a hit it returns the existing tracked row (the
/// candidate + policy + credentialRef the caller passed are discarded — the
/// spec's "registering the same (host, url) pair twice SHALL resolve to the
/// existing Repository row" contract). On a miss it adds the candidate
/// graph in one unit of work; the unique index
/// <c>ux_repositories_host_url</c> is the concurrency arbiter — two
/// concurrent callers racing into the same identity see one insert succeed
/// and the other surface <c>DbUpdateException</c>, at which point a retry
/// of the lookup resolves to the winner's row.
/// </para>
/// </summary>
/// <param name="db"></param>
public sealed class RepositoryStore(RepositoriesDbContext db) : IRepositoryStore
{
    /// <inheritdoc />
    public async Task<Repository?> FindByIdAsync(RepositoryId repositoryId, CancellationToken cancellationToken = default)
    {
        return await db.Repositories.SingleOrDefaultAsync(repository => repository.Id == repositoryId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Repository?> FindByIdentityAsync(string host, string url, CancellationToken cancellationToken = default)
    {
        var identity = RepositoryIdentity.Of(host, url);

        return await db.Repositories.SingleOrDefaultAsync(
            repository => repository.Host == identity.Host && repository.Url == identity.Url,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Repository>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await db.Repositories
            .AsNoTracking()
            .OrderBy(static repository => repository.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Repository> GetOrRegisterAsync(
        Repository candidate,
        RepositoryPolicy policy,
        RepositoryCredentialRef credentialRef,
        CancellationToken cancellationToken = default)
    {
        // Identity is computed off the candidate the caller built — same
        // normalization Repository.Create applies — so a re-registration
        // with casing/whitespace variants always queries for the stored key.
        var existing = await db.Repositories.SingleOrDefaultAsync(
            repository => repository.Host == candidate.Host && repository.Url == candidate.Url,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        db.Repositories.Add(candidate);
        db.RepositoryPolicies.Add(policy);
        db.RepositoryCredentialRefs.Add(credentialRef);

        await db.SaveChangesAsync(cancellationToken);

        return candidate;
    }

    /// <inheritdoc />
    public async Task SaveAsync(Repository repository, CancellationToken cancellationToken = default)
    {
        if (db.Entry(repository).State == EntityState.Detached)
        {
            db.Repositories.Add(repository);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
