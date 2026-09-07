using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.ApiKeys;
using Comuki.Modules.Identity.Domain.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Identity.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IApiKeyStore"/>. <see cref="FindByPrefixAsync"/>
/// is the auth handler's single indexed lookup; revoked rows are still
/// returned — revocation is a status, and the handler answers for it.
/// </summary>
/// <param name="db"></param>
public sealed class ApiKeyStore(IdentityDbContext db) : IApiKeyStore
{
    /// <inheritdoc />
    public async Task<ApiKey?> FindByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return await db.ApiKeys.SingleOrDefaultAsync(apiKey => apiKey.Prefix == prefix, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ApiKey?> FindByIdAsync(ApiKeyId apiKeyId, CancellationToken cancellationToken = default)
    {
        return await db.ApiKeys.SingleOrDefaultAsync(apiKey => apiKey.Id == apiKeyId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        if (db.Entry(apiKey).State == EntityState.Detached)
        {
            db.ApiKeys.Add(apiKey);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ApiKey> Items, int Total)> ListAsync(
        UserId? userId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var query = db.ApiKeys.AsNoTracking();

        if (userId is { } id)
        {
            query = query.Where(apiKey => apiKey.UserId == id);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(apiKey => apiKey.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
