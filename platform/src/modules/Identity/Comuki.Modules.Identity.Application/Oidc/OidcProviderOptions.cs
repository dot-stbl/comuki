using System.ComponentModel.DataAnnotations;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// One configured OIDC provider. The client secret is never in the
/// config file — <paramref name="ClientSecretEnv"/> names the
/// environment variable that holds it, resolved once at startup.
/// </summary>
public sealed class OidcProviderOptions
{
    /// <summary>Provider name — unique; used in scheme names and callback paths.</summary>
    [Required]
    public required string Name { get; init; }

    /// <summary>OIDC authority, e.g. <c>https://keycloak.example.com/realms/comuki</c>.</summary>
    [Required]
    public required string Authority { get; init; }

    /// <summary>Client id registered at the provider.</summary>
    [Required]
    public required string ClientId { get; init; }

    /// <summary>Name of the environment variable holding the client secret.</summary>
    [Required]
    public required string ClientSecretEnv { get; init; }

    /// <summary>
    /// Whether the authority must serve metadata over HTTPS. Defaults to
    /// true (the framework default); an explicit false admits an
    /// http:// authority — dev containers and local compose profiles
    /// only, never a production deployment.
    /// </summary>
    public bool? RequireHttps { get; init; }
}
