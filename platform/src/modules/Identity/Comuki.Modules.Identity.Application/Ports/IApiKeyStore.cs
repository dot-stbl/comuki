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
}
