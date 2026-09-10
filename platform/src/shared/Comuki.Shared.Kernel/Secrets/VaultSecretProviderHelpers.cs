using VaultSharp.V1.Commons;

namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Pure-logic helpers for <see cref="VaultSecretProvider"/> — extracted
/// so the provider stays free of <c>private static</c> helpers
/// (per <c>class-layout-and-tooling.md</c> §1a — no private methods in
/// production). <c>internal</c> because only
/// <see cref="VaultSecretProvider"/> in this assembly consumes the
/// helpers; the integration test project reaches them via
/// <c>InternalsVisibleTo</c> on <c>Comuki.Shared.Kernel.csproj</c>.
/// </summary>
internal static class VaultSecretProviderHelpers
{
    /// <summary>
    /// Stable cache key for one resolved Vault value. Includes the Vault
    /// address, mount, path, and field key so two providers (dev /
    /// staging) or two mounts against the same Vault never collide. The
    /// scheme prefix is implicit — only <c>vault:</c> refs route here,
    /// and the cache key never escapes this assembly.
    /// </summary>
    /// <param name="address">Vault server URL (from <see cref="VaultSecretOptions.Address"/>).</param>
    /// <param name="mount">K/V v2 mount point (from <see cref="VaultSecretOptions.KvMount"/>).</param>
    /// <param name="path">K/V v2 secret path (from <see cref="SecretRef.Path"/>).</param>
    /// <param name="key">Field key inside the K/V v2 secret (from <see cref="SecretRef.Key"/>).</param>
    public static string CacheKey(string address, string mount, string path, string key)
    {
        return $"vault:{address}:{mount}:{path}#{key}";
    }

    /// <summary>
    /// Extracts the requested field value from a VaultSharp V2 read
    /// response. Returns <c>null</c> when the response is null (network
    /// error / missing secret) or when the requested field is absent
    /// from the inner dict — the resolver turns the <c>null</c> into a
    /// <see cref="SecretRefUnsetException"/>. The field value is coerced
    /// to <see cref="string"/> via <see cref="object.ToString"/>; Vault
    /// stores string values natively, so the cast is lossless for the
    /// wire-format contract (secret refs carry strings).
    /// </summary>
    /// <param name="response">VaultSharp K/V v2 read response; nullable.</param>
    /// <param name="field">Field key inside the K/V v2 secret data.</param>
    public static string? ExtractFieldValue(Secret<SecretData>? response, string field)
    {
        return response?.Data?.Data is { } data && data.TryGetValue(field, out var raw)
            ? raw?.ToString()
            : null;
    }
}
