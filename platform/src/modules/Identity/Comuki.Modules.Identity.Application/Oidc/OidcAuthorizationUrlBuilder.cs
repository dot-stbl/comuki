namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Builds the authorize URL the host hands to the browser for the OIDC
/// code-flow. PKCE S256 is mandatory; <c>state</c> carries the host's
/// single-use token (a UUIDv7, opaque to the IdP); <c>redirect_uri</c>
/// is the unified <c>/api/v1/auth/oidc/callback</c> path.
/// </summary>
public static class OidcAuthorizationUrlBuilder
{
    /// <summary>Builds an authorize URL with the OIDC code-flow params.</summary>
    /// <param name="provider">The configured provider whose <c>Authority</c> owns the authorize endpoint.</param>
    /// <param name="authorizationEndpoint">Absolute URL of the IdP's authorize endpoint (from discovery or explicit config).</param>
    /// <param name="clientId">The client id registered at the IdP.</param>
    /// <param name="redirectUri">The callback URL the IdP will redirect to (unified per-deployment).</param>
    /// <param name="scope">Space-separated scopes; <c>openid</c> is mandatory, <c>profile email</c> is the usual minimum.</param>
    /// <param name="state">Single-use state token the IdP echoes back on callback.</param>
    /// <param name="codeChallenge">PKCE S256 challenge paired with the verifier the callback redeems.</param>
    public static Uri Build(
        OidcProviderOptions provider,
        Uri authorizationEndpoint,
        string clientId,
        string redirectUri,
        string scope,
        string state,
        string codeChallenge)
    {
        _ = provider;

        var builder = new UriBuilder(authorizationEndpoint)
        {
            Query = string.Empty,
        };

        var separator = '?';
        Append(builder, ref separator, "response_type", "code");
        Append(builder, ref separator, "client_id", clientId);
        Append(builder, ref separator, "redirect_uri", redirectUri);
        Append(builder, ref separator, "scope", scope);
        Append(builder, ref separator, "state", state);
        Append(builder, ref separator, "code_challenge", codeChallenge);
        Append(builder, ref separator, "code_challenge_method", "S256");

        return builder.Uri;
    }

    private static void Append(UriBuilder builder, ref char separator, string key, string value)
    {
        var encodedKey = Uri.EscapeDataString(key);
        var encodedValue = Uri.EscapeDataString(value);
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? $"{separator}{encodedKey}={encodedValue}"
            : $"{builder.Query}&{encodedKey}={encodedValue}";
        separator = '&';
    }
}
