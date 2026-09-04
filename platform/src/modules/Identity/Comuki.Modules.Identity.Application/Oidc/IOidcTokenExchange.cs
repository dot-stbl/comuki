namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Port over the IdP's token endpoint: code + PKCE in, id_token +
/// access_token out. Production impl is
/// <see cref="OidcTokenExchange"/> (form-encoded POST with Basic auth);
/// the port lets unit tests substitute a fake and skip the real HTTP.
/// </summary>
public interface IOidcTokenExchange
{
    /// <summary>Exchanges the authorization code at the IdP's token endpoint.</summary>
    /// <param name="tokenEndpoint"></param>
    /// <param name="clientId"></param>
    /// <param name="clientSecret"></param>
    /// <param name="code"></param>
    /// <param name="redirectUri"></param>
    /// <param name="codeVerifier"></param>
    /// <param name="cancellationToken"></param>
    public Task<OidcTokenResponse> ExchangeAsync(
        Uri tokenEndpoint,
        string clientId,
        string clientSecret,
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken = default);
}

/// <summary>The token-endpoint answer: id_token + access_token (and optional refresh).</summary>
public sealed record OidcTokenResponse(
    string IdToken,
    string AccessToken,
    string TokenType,
    int? ExpiresIn);
