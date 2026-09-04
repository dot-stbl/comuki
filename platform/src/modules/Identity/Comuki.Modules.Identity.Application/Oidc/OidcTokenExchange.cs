using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Default <see cref="IOidcTokenExchange"/>: form-encoded POST to the
/// IdP's token endpoint (per RFC 6749 §4.1.3) with Basic auth carrying
/// the client secret. Secrets never travel in the body.
/// </summary>
/// <param name="httpClient">Injected typed HTTP client.</param>
/// <param name="logger">Diagnostic log.</param>
public sealed class OidcTokenExchange(HttpClient httpClient, ILogger<OidcTokenExchange> logger) : IOidcTokenExchange
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

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(parameters),
        };

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

        var doc = System.Text.Json.JsonSerializer.Deserialize<TokenResponseDto>(body)
            ?? throw new InvalidOperationException("oidc token endpoint returned an empty body");

        return string.IsNullOrWhiteSpace(doc.IdToken)
            ? throw new InvalidOperationException("oidc token endpoint response is missing id_token")
            : new OidcTokenResponse(doc.IdToken, doc.AccessToken, doc.TokenType, doc.ExpiresIn);
    }

    private static string Truncate(string value, int max)
    {
        return value.Length <= max ? value : string.Concat(value.AsSpan(0, max), "…");
    }

    private sealed record TokenResponseDto(
        [property: JsonPropertyName("id_token")] string IdToken,
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn);
}
