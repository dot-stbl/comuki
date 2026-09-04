using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Caches the OIDC discovery document for a configured provider so the
/// authorize + token endpoints are paid once per TTL, not per request.
/// Plain HTTP + STJ on top of <see cref="HttpClient"/>; the response
/// shape is parsed into <see cref="OpenIdConnectConfiguration"/> (the
/// same one the framework's OpenIdConnect handler reads). Short TTL —
/// token endpoint rotations land without a restart but a hot login
/// flow doesn't refetch the well-known per request.
/// <para>
/// Keycloak 26+ emits fields like <c>frontchannel_logout_session_supported</c>
/// as <c>bool</c> while Microsoft's <see cref="OpenIdConnectConfiguration"/>
/// expects them as <c>string</c>; strict STJ deserialization of the
/// framework type rejects the doc. We hand-roll the four endpoints we
/// use (issuer, authorize, token, jwks_uri) and only populate the JWKS
/// signing keys — the rest of the doc is not consulted downstream.
/// </para>
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

    /// <summary>
    /// Public for tests — fetches the doc for a given well-known URL
    /// and maps just the four endpoints the host needs. Lenient on the
    /// shape: only the well-known field names matter; everything else
    /// (newer Keycloak fields, MS-typed-as-string but server-emits-bool,
    /// etc.) is ignored.
    /// </summary>
    /// <param name="wellKnown"></param>
    /// <param name="cancellationToken"></param>
    public async Task<OpenIdConnectConfiguration> FetchWithAuthorityAsync(string wellKnown, CancellationToken cancellationToken)
    {
        var body = await httpClient
            .GetStringAsync(wellKnown, cancellationToken)
            .ConfigureAwait(false);

        var config = new OpenIdConnectConfiguration();

        using (var doc = JsonDocument.Parse(body))
        {
            var root = doc.RootElement;

            if (TryGetString(root, "issuer", out var issuer))
            {
                config.Issuer = issuer;
            }

            if (TryGetString(root, "authorization_endpoint", out var authorize))
            {
                config.AuthorizationEndpoint = authorize;
            }

            if (TryGetString(root, "token_endpoint", out var token))
            {
                config.TokenEndpoint = token;
            }

            if (TryGetString(root, "jwks_uri", out var jwksUri))
            {
                config.JwksUri = jwksUri;
            }
        }

        if (!string.IsNullOrWhiteSpace(config.JwksUri))
        {
            var jwksBody = await httpClient
                .GetStringAsync(config.JwksUri, cancellationToken)
                .ConfigureAwait(false);

            foreach (var key in new JsonWebKeySet(jwksBody).GetSigningKeys())
            {
                config.SigningKeys.Add(key);
            }
        }

        return string.IsNullOrWhiteSpace(config.Issuer)
            || string.IsNullOrWhiteSpace(config.AuthorizationEndpoint)
            || string.IsNullOrWhiteSpace(config.TokenEndpoint)
            ? throw new InvalidOperationException(
                $"discovery document at '{wellKnown}' is missing required fields "
                + "(issuer, authorization_endpoint, token_endpoint)")
            : config;
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
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
