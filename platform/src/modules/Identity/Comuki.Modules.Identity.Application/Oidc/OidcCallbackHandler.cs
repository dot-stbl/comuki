using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Oidc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Owns the manual OIDC code-flow callback: state validation, code
/// exchange at the IdP's token endpoint, id_token signature
/// verification, account linking, and the cookie sign-in. The host
/// wires its callback controller to this handler and renders the
/// returned <see cref="OidcCallbackResult.RedirectTarget"/> as a 302.
/// </summary>
/// <param name="stateStore">Persistence port for the single-use state row.</param>
/// <param name="discovery">Cached discovery doc (authorize + token + JWKS).</param>
/// <param name="options">Configured providers list.</param>
/// <param name="clientSecrets">Env-var lookup for the per-provider client secret.</param>
/// <param name="tokenExchange">Token endpoint POST (form-encoded + Basic auth).</param>
/// <param name="idTokenValidator">JWKS-backed JWT signature verification.</param>
/// <param name="linker">Existing account-or-link resolver.</param>
/// <param name="signer">Cookie sign-in: <see cref="ICookieSigner"/> is host-side.</param>
/// <param name="logger">Diagnostic log.</param>
public sealed class OidcCallbackHandler(
    IOidcStateStore stateStore,
    IOidcDiscovery discovery,
    IOptions<OidcOptions> options,
    IOidcClientSecrets clientSecrets,
    IOidcTokenExchange tokenExchange,
    IOidcIdTokenValidator idTokenValidator,
    OidcAccountLinker linker,
    ICookieSigner signer,
    ILogger<OidcCallbackHandler> logger)
{
    /// <summary>Default landing path on success — operator may have come in cold.</summary>
    private const string DefaultReturnTo = "/";

    /// <summary>
    /// Processes the IdP's authorize redirect: validates state, runs
    /// the code-for-tokens exchange, verifies the id_token, links or
    /// provisions the local account, and signs the user in via the
    /// cookie scheme. Any failure short-circuits with a stable failure
    /// code the SPA can surface.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    public async Task<OidcCallbackResult> HandleAsync(OidcCallbackRequest request, CancellationToken cancellationToken = default)
    {
        // Provider-side failure — surface what the IdP told us, never invent one.
        if (!string.IsNullOrWhiteSpace(request.Error))
        {
            logger.LogInformation(
                "Oidc callback returned provider error {Error}: {Description}",
                request.Error,
                request.ErrorDescription);

            return new OidcCallbackResult(
                Success: false,
                RedirectTarget: BuildLoginRedirect($"oidc.provider_{request.Error}"),
                FailureCode: $"oidc.provider_{request.Error}");
        }

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.State))
        {
            return new OidcCallbackResult(
                Success: false,
                RedirectTarget: BuildLoginRedirect("oidc.callback_incomplete"),
                FailureCode: "oidc.callback_incomplete");
        }

        if (!Guid.TryParse(request.State, out var stateGuid))
        {
            return new OidcCallbackResult(
                Success: false,
                RedirectTarget: BuildLoginRedirect("oidc.state_malformed"),
                FailureCode: "oidc.state_malformed");
        }

        var stateRow = await stateStore.ConsumeAsync(new OidcStateId(stateGuid), cancellationToken);
        if (stateRow is null)
        {
            return new OidcCallbackResult(
                Success: false,
                RedirectTarget: BuildLoginRedirect("oidc.state_mismatch"),
                FailureCode: "oidc.state_mismatch");
        }

        var provider = ResolveProvider(stateRow.Provider);
        var discoveryDoc = await discovery.GetAsync(provider, cancellationToken);

        if (string.IsNullOrWhiteSpace(discoveryDoc.TokenEndpoint))
        {
            logger.LogWarning("Oidc provider {Provider} discovery has no token_endpoint", provider.Name);
            return new OidcCallbackResult(
                Success: false,
                RedirectTarget: BuildLoginRedirect("oidc.token_endpoint_missing"),
                FailureCode: "oidc.token_endpoint_missing");
        }

        var secret = await clientSecrets.GetAsync(provider.Name, cancellationToken);

        OidcTokenResponse token;
        try
        {
            token = await tokenExchange.ExchangeAsync(
                new Uri(discoveryDoc.TokenEndpoint, UriKind.Absolute),
                provider.ClientId,
                secret,
                request.Code,
                stateRow.RedirectUri,
                stateRow.CodeVerifier,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Oidc token exchange failed for provider {Provider}", provider.Name);
            return new OidcCallbackResult(
                Success: false,
                RedirectTarget: BuildLoginRedirect("oidc.token_exchange_failed"),
                FailureCode: "oidc.token_exchange_failed");
        }

        OidcVerifiedClaims claims;
        try
        {
            claims = idTokenValidator.Validate(token.IdToken, discoveryDoc, provider.ClientId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Oidc id_token validation failed for provider {Provider}", provider.Name);
            return new OidcCallbackResult(
                Success: false,
                RedirectTarget: BuildLoginRedirect("oidc.id_token_invalid"),
                FailureCode: "oidc.id_token_invalid");
        }

        var linkResult = await linker.HandleAsync(
            new OidcLinkRequest(provider.Name, claims.Subject, claims.Email, claims.DisplayName),
            cancellationToken);

        if (linkResult.Created)
        {
            logger.LogInformation(
                "Oidc provider {Provider} provisioned local account {Email}",
                provider.Name,
                claims.Email);
        }

        await signer.SignInAsync(linkResult.User, cancellationToken);

        var returnTo = stateRow.ReturnTo is { Length: > 0 } candidate
            && candidate.StartsWith('/')
            && !candidate.StartsWith("//")
            && !candidate.StartsWith("/\\")
            ? candidate
            : DefaultReturnTo;

        return new OidcCallbackResult(Success: true, RedirectTarget: returnTo, FailureCode: null);
    }

    private OidcProviderOptions ResolveProvider(string name)
    {
        return options.Value.Providers
            .FirstOrDefault(configured =>
                string.Equals(configured.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"oidc provider '{name}' is not configured");
    }

    private static string BuildLoginRedirect(string failureCode)
    {
        return $"/login?reason=oidc-failed&error={Uri.EscapeDataString(failureCode)}";
    }
}
