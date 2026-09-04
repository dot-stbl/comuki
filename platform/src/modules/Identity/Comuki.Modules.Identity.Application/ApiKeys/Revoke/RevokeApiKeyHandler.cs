using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Ids;

namespace Comuki.Modules.Identity.Application.ApiKeys.Revoke;

/// <summary>
/// Revokes an API key — idempotent (a second revoke on a revoked key
/// returns the same view). Revocation is a timestamp, never a delete;
/// the prefix stays burned for audit.
/// </summary>
/// <param name="apiKeyStore"></param>
/// <param name="clock"></param>
public sealed class RevokeApiKeyHandler(IApiKeyStore apiKeyStore, TimeProvider clock)
{
    /// <summary>Revokes the key.</summary>
    /// <param name="apiKeyId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">Unknown api key id.</exception>
    public async Task<ApiKeyView> HandleAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        var typedId = new ApiKeyId(apiKeyId);
        var apiKey = await apiKeyStore.FindByIdAsync(typedId, cancellationToken)
            ?? throw new InvalidOperationException($"api key {typedId} not found");

        apiKey.Revoke(clock.GetUtcNow());
        await apiKeyStore.SaveAsync(apiKey, cancellationToken);

        return ApiKeyView.Of(apiKey);
    }
}
