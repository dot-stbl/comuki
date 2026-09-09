using Microsoft.Extensions.Options;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Resolves a single <see cref="OidcProviderOptions"/> from the bound
/// <see cref="OidcOptions"/> by name. The lookup is case-insensitive
/// against the configured provider names. Throws
/// <see cref="InvalidOperationException"/> when the named provider is
/// not configured — the call sites translate that into a deny / failure
/// response with a stable machine-readable code.
/// </summary>
/// <remarks>
/// Creates the resolver.
/// </remarks>
/// <param name="options">Bound OIDC options (configuration-derived).</param>
public sealed class OidcProviderResolver(IOptions<OidcOptions> options)
{
    private readonly IOptions<OidcOptions> options = options;

    /// <summary>
    /// Returns the provider with the matching <paramref name="name"/>.
    /// </summary>
    /// <param name="name">Provider name as it appears in the OIDC options.</param>
    /// <exception cref="InvalidOperationException">The provider is not configured.</exception>
    public OidcProviderOptions Resolve(string name)
    {
        return options.Value.Providers
            .FirstOrDefault(configured =>
                string.Equals(configured.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"oidc provider '{name}' is not configured");
    }
}
