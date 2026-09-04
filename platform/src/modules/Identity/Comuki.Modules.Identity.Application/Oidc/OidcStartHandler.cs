using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Oidc;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Owns the manual OIDC code-flow start: PKCE generation, state row
/// persistence, authorize-URL build. The host wires its start
/// controller to this handler and answers with a 302 to the result.
/// </summary>
/// <param name="discovery">Cached discovery-doc retriever (authorize + token endpoints + JWKS).</param>
/// <param name="stateStore">Persistence port for the state row carrying the verifier + returnTo.</param>
/// <param name="options">Provider configuration.</param>
/// <param name="clientSecrets">Map from provider name to the resolved client secret (env-var lookup at startup).</param>
/// <param name="clock">Time provider — TTL of the state row.</param>
public sealed class OidcStartHandler(
    IOidcDiscovery discovery,
    IOidcStateStore stateStore,
    IOptions<OidcOptions> options,
    OidcClientSecrets clientSecrets,
    TimeProvider clock)
{
    /// <summary>5 minutes — long enough for a slow IdP, short enough that a leaked state expires.</summary>
    private static readonly TimeSpan stateTtl = TimeSpan.FromMinutes(5);

    /// <summary>Default scopes when the deployment does not override.</summary>
    private const string DefaultScope = "openid profile email";

    /// <summary>Per-provider OIDC client secret lookup — kept distinct from <see cref="OidcProviderOptions"/> so the secret never leaks to logs.</summary>
    /// <param name="providerName"></param>
    /// <param name="cancellationToken"></param>
    public Task<string> GetClientSecretAsync(string providerName, CancellationToken cancellationToken = default)
    {
        return clientSecrets.GetAsync(providerName, cancellationToken);
    }

    /// <summary>
    /// Issues an in-flight state row and returns the authorize URL the
    /// browser must hit next.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidOperationException">Unknown provider.</exception>
    public async Task<OidcStartResult> HandleAsync(OidcStartRequest request, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(request.Provider);

        var discoveryDoc = await discovery.GetAsync(provider, cancellationToken);
        if (string.IsNullOrWhiteSpace(discoveryDoc.AuthorizationEndpoint))
        {
            throw new InvalidOperationException(
                $"oidc provider '{provider.Name}' discovery has no authorization_endpoint");
        }

        var pair = OidcPkce.Generate();
        var now = clock.GetUtcNow();

        var state = OidcState.Create(
            provider.Name,
            pair.Verifier,
            "S256",
            request.RedirectUri,
            request.ReturnTo,
            now,
            stateTtl);
        await stateStore.SaveAsync(state, cancellationToken);

        var url = OidcAuthorizationUrlBuilder.Build(
            provider,
            new Uri(discoveryDoc.AuthorizationEndpoint, UriKind.Absolute),
            provider.ClientId,
            request.RedirectUri,
            request.Scope ?? DefaultScope,
            state.Id.Value.ToString("D"),
            pair.Challenge);

        return new OidcStartResult(url, state.Id.Value.ToString("D"));
    }

    private OidcProviderOptions ResolveProvider(string name)
    {
        var match = options.Value.Providers
            .FirstOrDefault(configured =>
                string.Equals(configured.Name, name, StringComparison.OrdinalIgnoreCase));

        return match
            ?? throw new InvalidOperationException(
                $"oidc provider '{name}' is not configured");
    }
}
