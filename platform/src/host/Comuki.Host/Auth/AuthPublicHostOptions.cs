using System.ComponentModel.DataAnnotations;

namespace Comuki.Host.Auth;

/// <summary>
/// The configured public host the host advertises to OIDC IdPs
/// (security audit A03-1 / A10-1). The OIDC <c>redirect_uri</c> MUST
/// be built from this configured URL — not from the inbound
/// <c>Host:</c> header, which is attacker-controlled and lets a
/// poisoned reverse-proxy redirect the IdP's <c>code</c> to an
/// arbitrary origin.
/// <para>
/// Bind from <c>auth:publicHost</c> via <c>AddOptions&lt;...&gt;</c> at
/// composition root. The scheme + host are concatenated into an
/// absolute URL the IdP registers; the OIDC callback path
/// (<c>/api/v1/auth/oidc/callback</c>) is appended by
/// <see cref="PublicHost.RedirectUriBuilder"/>.
/// </para>
/// </summary>
public sealed class AuthPublicHostOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "auth:publicHost";

    /// <summary>
    /// Env var holding the absolute public URL (scheme + host, no
    /// trailing path). Operators set this per deployment — no Host
    /// header ever reaches the OIDC flow.
    /// </summary>
    public const string PublicUrlEnvVariable = "COMUKI_PUBLIC_HOST_URL";

    /// <summary>Absolute public URL (scheme + host, no trailing path). Empty when the host is unconfigured.</summary>
    [Required]
    public string PublicUrl { get; init; } = string.Empty;

    /// <summary>
    /// Resolves the effective options: the bound section first, then
    /// the env-var key read via the configuration provider chain (which
    /// in production is backed by <see cref="Environment.GetEnvironmentVariable(string)"/>
    /// — the env vars provider lifts <c>COMUKI_PUBLIC_HOST_URL</c>
    /// into <see cref="IConfiguration"/> under that key). Empty result
    /// means "unconfigured" — the host will not boot until the operator
    /// supplies one (the OIDC start endpoint fails with 404 on missing
    /// config + 500 on empty URL).
    /// </summary>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static AuthPublicHostOptions Resolve(IConfiguration configuration)
    {
        var bound = configuration.GetSection(SectionName).Get<AuthPublicHostOptions>() ?? new AuthPublicHostOptions();
        var url = !string.IsNullOrWhiteSpace(bound.PublicUrl)
            ? bound.PublicUrl
            : configuration[PublicUrlEnvVariable];

        return new AuthPublicHostOptions { PublicUrl = url ?? string.Empty };
    }
}
