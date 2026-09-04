using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Exchanges the authorization code at the IdP's token endpoint for
/// id_token + access_token. PKCE carries the verifier — without it
/// the IdP rejects the exchange. Confidential clients also send the
/// client secret in a Basic Authorization header.
/// </summary>
/// <param name="httpClient">Injected typed HTTP client.</param>
/// <param name="logger">Diagnostic log.</param>
public sealed class OidcTokenExchange(HttpClient httpClient, ILogger<OidcTokenExchange> logger)
{
    /// <summary>The token-endpoint answer: id_token + access_token (and optional refresh).</summary>
    public sealed record TokenResponse(
        [property: JsonPropertyName("id_token")] string IdToken,
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn);

    /// <summary>
    /// POSTs the authorization code to the IdP's token endpoint. The
    /// request is form-encoded (per RFC 6749 §4.1.3) and the client
    /// secret is sent via Basic auth — secrets never travel in the body.
    /// </summary>
    /// <param name="tokenEndpoint"></param>
    /// <param name="clientId"></param>
    /// <param name="clientSecret"></param>
    /// <param name="code"></param>
    /// <param name="redirectUri">Must match the redirect_uri used at authorize time.</param>
    /// <param name="codeVerifier">PKCE verifier paired with the challenge sent at authorize time.</param>
    /// <param name="cancellationToken"></param>
    public async Task<TokenResponse> ExchangeAsync(
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

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(parameters),
        };

        // Confidential-client Basic auth — never the client_secret in the body.
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Oidc token exchange returned {Status}: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException(
                $"oidc token endpoint returned {(int)response.StatusCode}: {Truncate(body, 256)}");
        }

        var doc = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(body)
            ?? throw new InvalidOperationException("oidc token endpoint returned an empty body");

        return string.IsNullOrWhiteSpace(doc.IdToken)
            ? throw new InvalidOperationException("oidc token endpoint response is missing id_token")
            : doc;
    }

    private static string Truncate(string value, int max)
    {
        return value.Length <= max ? value : string.Concat(value.AsSpan(0, max), "…");
    }
}
