using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Resolves OIDC client secrets from the secret reference named in
/// <see cref="OidcProviderOptions.ClientSecretEnv"/>. Singleton — each
/// provider's secret is resolved once via the shared-kernel
/// <see cref="ISecretResolver"/> at first lookup and cached for the
/// process lifetime. The resolver is async (vault / consul are HTTP in
/// future slices) so the lookup path is <see cref="Task{TResult}"/>-
/// returning; the cache hides the cost after the first call. Rotation
/// invalidates every stored value by design — deploy a fresh secret via
/// the deployment's secret store and restart.
/// </summary>
/// <param name="options">Provider configuration (names + secret refs).</param>
/// <param name="secrets">Shared-kernel resolver — routes by scheme to the matching provider.</param>
public sealed class OidcClientSecrets(IOptions<OidcOptions> options, ISecretResolver secrets) : IOidcClientSecrets
{
    private readonly Dictionary<string, string> resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock gate = new();

    /// <inheritdoc />
    public async Task<string> GetAsync(string providerName, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (resolved.TryGetValue(providerName, out var cached))
            {
                return cached;
            }
        }

        // The configuration lookup is not thread-safe (Options.Value iterates a
        // List<>) — we accept that and rely on the lock above to serialize the
        // per-provider first-lookup. The resolver call itself is async-safe.
        var provider = options.Value.Providers
            .FirstOrDefault(configured =>
                string.Equals(configured.Name, providerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"oidc provider '{providerName}' is not configured");

        var reference = provider.ClientSecretEnv;
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidOperationException(
                $"oidc provider '{providerName}' has no ClientSecretEnv configured");
        }

        var secret = await secrets.ResolveAsync(reference, cancellationToken);
        // The resolver contract: a non-empty reference either returns
        // a non-empty value or throws SecretRefUnsetException. The guard
        // below is defensive only — production paths never reach it.
        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException(
                $"oidc provider '{providerName}': secret reference '{reference}' resolved empty");
        }

        lock (gate)
        {
            // Double-check under the gate — another thread may have populated
            // the entry while we awaited the resolver.
            if (resolved.TryGetValue(providerName, out var concurrent))
            {
                return concurrent;
            }

            resolved[providerName] = secret;
        }

        return secret;
    }
}
