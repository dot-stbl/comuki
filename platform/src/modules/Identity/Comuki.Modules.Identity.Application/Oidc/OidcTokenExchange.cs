using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Default <see cref="IOidcTokenExchange"/>: form-encoded POST to the
/// IdP's token endpoint (per RFC 6749 §4.1.3) with Basic auth carrying
/// the client secret. Secrets never travel in the body.
/// <para>
/// Refit-typed: the wire shape lives in <see cref="IOidcTokenExchangeApi"/>;
/// this class carries the application-level concerns (Basic auth header
/// construction, post-response validation, domain mapping to
/// <see cref="OidcTokenResponse"/>). The <c>HttpClient</c> is injected
/// as a Refit-managed client.
/// </para>
/// </summary>
/// <param name="api">Refit-generated OIDC token client (DI-registered in <c>IdentityApplicationExtensions</c>).</param>
/// <param name="logger">Diagnostic log.</param>
public sealed class OidcTokenExchange(IOidcTokenExchangeApi api, ILogger<OidcTokenExchange> logger) : IOidcTokenExchange
{
    /// <inheritdoc />
    public async Task<OidcTokenResponse> ExchangeAsync(
        Uri tokenEndpoint,
        string clientId,
        string clientSecret,
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
        };

        // boundary: client secret rides ONLY in this Basic auth
        // header — never in the body, never in logs, never in a query
        // string. The header is per-request; the secret is read from
        // the call site (already in memory at this point).
        var basic = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}"));

        try
        {
            var doc = await api.ExchangeAsync(
                tokenEndpoint,
                parameters,
                $"Basic {basic}",
                cancellationToken);

            return string.IsNullOrWhiteSpace(doc.IdToken)
                ? throw new InvalidOperationException("oidc token endpoint response is missing id_token")
                : new OidcTokenResponse(doc.IdToken, doc.AccessToken, doc.TokenType, doc.ExpiresIn);
        }
        catch (Refit.ApiException exception)
        {
            // boundary: the IdP rejected the exchange. The Refit
            // exception carries the HTTP status + the body (often a
            // JSON error object); we log the truncated body for the
            // operator and surface a single-line error to the caller.
            var status = (int)exception.StatusCode;
            var body = OidcResponseText.Truncate(exception.Content ?? string.Empty, 256);
            logger.LogWarning("Oidc token exchange returned {Status}: {Body}", status, body);
            throw new InvalidOperationException(
                $"oidc token endpoint returned {status}: {body}");
        }
    }
}
