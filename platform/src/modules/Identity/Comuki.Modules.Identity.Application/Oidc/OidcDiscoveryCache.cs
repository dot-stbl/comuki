using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Caches the OIDC discovery document for a configured provider so the
/// authorize + token endpoints are paid once per TTL, not per request.
/// Plain HTTP + STJ on top of <see cref="HttpClient"/>; the response
/// shape is parsed into <see cref="OpenIdConnectConfiguration"/> (the
/// same one the framework's OpenIdConnect handler reads). Short TTL —
/// token endpoint rotations land without a restart but a hot login
/// flow doesn't refetch the well-known per request.
/// </summary>
/// <param name="cache"></param>
/// <param name="httpClient">Injected — uses the typed client the host registers.</param>
public sealed class OidcDiscoveryCache(IMemoryCache cache, HttpClient httpClient) : IOidcDiscovery
{
    /// <summary>5 minutes — token endpoint rotations are rare and a stale endpoint fails the next exchange.</summary>
    private static readonly TimeSpan ttl = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public async Task<OpenIdConnectConfiguration> GetAsync(OidcProviderOptions provider, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = CacheKey(provider);

        if (cache.TryGetValue<OpenIdConnectConfiguration>(cacheKey, out var cachedDoc) && cachedDoc is not null)
        {
            return cachedDoc;
        }

        var wellKnown = BuildWellKnown(provider.Authority);
        var doc = await FetchWithAuthorityAsync(wellKnown, cancellationToken).ConfigureAwait(false);

        cache.Set(cacheKey, doc, ttl);

        return doc;
    }

    /// <summary>Public for tests — fetches the doc for a given well-known URL.</summary>
    /// <param name="wellKnown"></param>
    /// <param name="cancellationToken"></param>
    public async Task<OpenIdConnectConfiguration> FetchWithAuthorityAsync(string wellKnown, CancellationToken cancellationToken)
    {
        return await httpClient
            .GetFromJsonAsync<OpenIdConnectConfiguration>(wellKnown, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"discovery document at '{wellKnown}' answered an empty body");
    }

    private static string BuildWellKnown(string authority)
    {
        var trimmed = authority.TrimEnd('/');
        return $"{trimmed}/.well-known/openid-configuration";
    }

    private static string CacheKey(OidcProviderOptions provider)
    {
        return $"oidc:discovery:{provider.Name.Trim().ToLowerInvariant()}";
    }
}
