namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Inputs to the OIDC start flow: a configured provider name, the
/// unified callback URL the IdP will redirect to, an optional in-app
/// return path the operator was bounced from.
/// </summary>
/// <param name="Provider">Provider name as configured in <c>auth:oidc:providers</c>.</param>
/// <param name="RedirectUri">Absolute URL of the unified <c>/api/v1/auth/oidc/callback</c>.</param>
/// <param name="ReturnTo">Optional in-app path the operator was bounced from.</param>
/// <param name="Scope">Space-separated scopes; defaults to <c>openid profile email</c> when null.</param>
public sealed record OidcStartRequest(
    string Provider,
    string RedirectUri,
    string? ReturnTo,
    string? Scope = null);

/// <summary>
/// The authorize URL the browser must navigate to, plus the state token
/// the host issued (URL-encoded into <c>state=</c> by the builder).
/// The state token is also persisted to the store under the hood —
/// callers do not need to track it.
/// </summary>
/// <param name="AuthorizeUrl">Absolute URL of the IdP's authorize endpoint with all OAuth2 params.</param>
/// <param name="StateToken">The single-use state token the IdP will echo back.</param>
public sealed record OidcStartResult(Uri AuthorizeUrl, string StateToken);
