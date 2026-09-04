using Microsoft.Extensions.Options;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Resolves OIDC client secrets from the environment variables named
/// in <see cref="OidcProviderOptions.ClientSecretEnv"/>. Singleton —
/// secrets are read once per provider at first lookup, then cached for
/// the process lifetime. Rotation invalidates every stored digest by
/// design (deploy a fresh secret via the deployment's secret store).
/// </summary>
/// <param name="options">Provider configuration (names + secret env-var names).</param>
public sealed class OidcClientSecrets(IOptions<OidcOptions> options) : IOidcClientSecrets
{
    private readonly Dictionary<string, string> resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock gate = new();

    /// <inheritdoc />
    public Task<string> GetAsync(string providerName, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        lock (gate)
        {
            if (resolved.TryGetValue(providerName, out var cached))
            {
                return Task.FromResult(cached);
            }

            var provider = options.Value.Providers
                .FirstOrDefault(configured =>
                    string.Equals(configured.Name, providerName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"oidc provider '{providerName}' is not configured");

            var envName = provider.ClientSecretEnv;
            if (string.IsNullOrWhiteSpace(envName))
            {
                throw new InvalidOperationException(
                    $"oidc provider '{providerName}' has no ClientSecretEnv configured");
            }

            var secret = Environment.GetEnvironmentVariable(envName)
                ?? throw new InvalidOperationException(
                    $"oidc provider '{providerName}': environment variable '{envName}' with the client secret is not set");

            resolved[providerName] = secret;

            return Task.FromResult(secret);
        }
    }
}
