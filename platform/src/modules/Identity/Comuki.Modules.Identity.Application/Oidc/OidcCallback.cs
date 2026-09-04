namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Inputs to the OIDC callback handler: the IdP's authorize redirect
/// hands us back <c>code</c> + <c>state</c> on success, or
/// <c>error</c> + <c>error_description</c> on user-visible failures.
/// </summary>
/// <param name="Code">Authorization code the IdP issued — exchanged at the token endpoint.</param>
/// <param name="State">Single-use state token the host issued; matches a state row.</param>
/// <param name="Error">Error code returned by the IdP (e.g. <c>access_denied</c>); null on success.</param>
/// <param name="ErrorDescription">Optional human-readable description of <paramref name="Error"/>.</param>
public sealed record OidcCallbackRequest(
    string? Code,
    string? State,
    string? Error,
    string? ErrorDescription);

/// <summary>
/// What the callback handler decides: redirect target on success, or
/// the failure code the SPA should display. The success path also
/// confirms the cookie session was set.
/// </summary>
/// <param name="Success">True when the user has been signed in.</param>
/// <param name="RedirectTarget">Absolute or in-app URL to redirect to.</param>
/// <param name="FailureCode">Stable machine code the SPA surfaces (e.g. <c>oidc.state_mismatch</c>).</param>
public sealed record OidcCallbackResult(
    bool Success,
    string RedirectTarget,
    string? FailureCode);
