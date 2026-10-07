using Refit;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Refit interface for the OIDC token-endpoint POST. The
/// token endpoint URL varies per provider (and per
/// <see cref="OidcProviderOptions"/>), so the URL is passed as a
/// method parameter rather than the configured base address.
/// <para>
/// Discovery is intentionally NOT covered here: the OIDC discovery
/// document (RFC 8414 / OIDC discovery 1.0 §4) is hand-parsed in
/// <see cref="OidcDiscoveryCache"/> because Keycloak 26+ emits fields
/// like <c>frontchannel_logout_session_supported</c> as <c>bool</c> while
/// Microsoft's <see cref="Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration"/>
/// models the same fields as <c>string</c>. Strict STJ deserialization
/// of the framework type rejects the document; hand-rolled parsing
/// pulls only the four endpoints and the JWKS signing keys the
/// platform needs, ignoring the shape mismatch. Refitting discovery
/// is the right long-term move (custom JsonConverter on the framework
/// type) but is a separate piece of work — see
/// <c>class-layout-and-tooling.md</c> §1a п.8 exemption note for the
/// parser/lexer state archetype and the boundary comment in
/// <see cref="OidcDiscoveryCache"/> for the same rationale.
/// </para>
/// </summary>
public interface IOidcTokenExchangeApi
{
    /// <summary>POST <paramref name="tokenEndpoint"/> with the form-encoded token-exchange body and Basic auth. RFC 6749 §4.1.3.</summary>
    /// <param name="tokenEndpoint">Absolute token endpoint URL — varies per provider; passed per call instead of as a base address.</param>
    /// <param name="form">Form-encoded body: grant_type, client_id, code, redirect_uri, code_verifier.</param>
    /// <param name="basicAuthorization">Pre-computed <c>Authorization: Basic base64(client_id:client_secret)</c> header value.</param>
    /// <param name="cancellationToken">Forwarded to the HTTP client.</param>
    [Post("")]
    public Task<TokenExchangeResponse> ExchangeAsync(
        Uri tokenEndpoint,
        [Body(BodySerializationMethod.UrlEncoded)] IDictionary<string, string> form,
        [Header("Authorization")] string basicAuthorization,
        CancellationToken cancellationToken = default);
}

/// <summary>Wire shape of the RFC 6749 §5.1 successful token response — id_token is the load-bearing field for the manual code flow.</summary>
/// <param name="IdToken">The OIDC ID token; the only field the platform currently consumes.</param>
/// <param name="AccessToken">OAuth 2.0 access token (often absent in pure-OIDC code flows).</param>
/// <param name="TokenType">Token type (typically <c>"Bearer"</c>).</param>
/// <param name="ExpiresIn">Lifetime in seconds; null when the IdP omits the field.</param>
public sealed record TokenExchangeResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("id_token")] string IdToken,
    [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
    [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string TokenType,
    [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int? ExpiresIn);
