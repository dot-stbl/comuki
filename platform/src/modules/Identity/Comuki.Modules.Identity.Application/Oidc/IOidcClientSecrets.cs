namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Resolves the per-provider OIDC client secret at startup. The secret
/// is read from the environment variable named in
/// <see cref="OidcProviderOptions.ClientSecretEnv"/>
/// — the secret value never lands in a log line, a config file, or a
/// request body. The lookup is keyed on provider name because multiple
/// providers can coexist.
/// </summary>
public interface IOidcClientSecrets
{
    /// <summary>Returns the secret for <paramref name="providerName"/> or throws.</summary>
    /// <param name="providerName"></param>
    /// <param name="cancellationToken"></param>
    public Task<string> GetAsync(string providerName, CancellationToken cancellationToken = default);
}
