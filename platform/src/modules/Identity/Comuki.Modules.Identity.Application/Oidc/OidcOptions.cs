namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// OIDC configuration root, bound from <c>auth:oidc</c>: a (possibly
/// empty) list of providers. An empty list is legitimate — local login
/// works without any IdP; providers are opt-in per deployment.
/// </summary>
public sealed class OidcOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "auth:oidc";

    /// <summary>Configured providers; empty when OIDC is off.</summary>
    public List<OidcProviderOptions> Providers { get; init; } = [];
}
