using Comuki.Modules.Identity.Domain.ApiKeys;
using Comuki.Modules.Identity.Domain.Ids;

namespace Comuki.Modules.Identity.Application.Ports;

/// <summary>Persistence port for API keys.</summary>
public interface IApiKeyStore
{
    /// <summary>Finds a key row by its public prefix (indexed single lookup).</summary>
    /// <param name="prefix"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<ApiKey?> FindByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Finds a key row by id.</summary>
    /// <param name="apiKeyId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<ApiKey?> FindByIdAsync(ApiKeyId apiKeyId, CancellationToken cancellationToken = default);

    /// <summary>Persists a new or changed key row.</summary>
    /// <param name="apiKey"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task SaveAsync(ApiKey apiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a page of API keys (active + revoked rows). Optional user-id
    /// filter narrows the page to a single user.
    /// </summary>
    /// <param name="userId">Optional owner user id filter; <c>null</c> for any.</param>
    /// <param name="skip">Number of rows to skip (<c>(page - 1) * pageSize</c>).</param>
    /// <param name="take">Page size (1..100).</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The page slice and the total row count.</returns>
    public Task<(IReadOnlyList<ApiKey> Items, int Total)> ListAsync(
        UserId? userId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
