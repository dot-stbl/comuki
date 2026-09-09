namespace Comuki.Host.Auth.PublicHost;

/// <summary>
/// Pure-function builder for the OIDC <c>redirect_uri</c> sent to the
/// IdP on the authorize step. The input is the configured public host
/// (<see cref="AuthPublicHostOptions"/>) — never the inbound
/// <c>Host</c> header — so a poisoned reverse proxy or direct
/// attacker cannot weaponise the request to redirect the IdP's
/// <c>code</c> to an arbitrary origin (security audit A03-1 / A10-1).
/// </summary>
internal static class RedirectUriBuilder
{
    /// <summary>
    /// Returns the absolute callback URL the IdP is configured with:
    /// the configured public URL + the unified callback path.
    /// </summary>
    /// <param name="publicUrl">Configured <c>auth:publicHost:publicUrl</c> — scheme + host, no trailing path.</param>
    /// <returns>Absolute URL string suitable for the IdP's <c>redirect_uri</c>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="publicUrl"/> is empty or whitespace — the host
    /// is misconfigured.
    /// </exception>
    public static string Build(string publicUrl)
    {
        if (string.IsNullOrWhiteSpace(publicUrl))
        {
            throw new InvalidOperationException(
                "auth:publicHost:publicUrl (or COMUKI_PUBLIC_HOST_URL) is required for the OIDC start flow; "
                + "set the configured public URL so the IdP redirect_uri cannot be poisoned by the inbound Host header");
        }

        var path = $"/{ApiRoutes.AuthOidcRoot}/callback";

        return $"{publicUrl.TrimEnd('/')}{path}";
    }
}
