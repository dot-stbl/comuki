using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Port for fetching the OIDC discovery document for a configured
/// provider. The cached shape answers "where do I send the browser?"
/// and "where do I exchange the code?" — both required at start/callback
/// time. Implementations cache the document for a short TTL so a hot
/// login flow doesn't refetch the well-known endpoint per request.
/// </summary>
public interface IOidcDiscovery
{
    /// <summary>Fetches (or returns the cached) discovery document for <paramref name="provider"/>.</summary>
    /// <param name="provider"></param>
    /// <param name="cancellationToken"></param>
    public Task<OpenIdConnectConfiguration> GetAsync(OidcProviderOptions provider, CancellationToken cancellationToken = default);
}
