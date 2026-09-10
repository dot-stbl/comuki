using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VaultSharp;

namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// HashiCorp Vault-backed secret provider (issue #52, slice 2). Reads
/// K/V v2 secret values through <see cref="IVaultClient"/>. The
/// bootstrap Vault token is baked into the
/// <see cref="IVaultClient"/> at startup by the DI factory in
/// <c>HostComposer</c> (fail-fast in Production when the env var named
/// in <see cref="VaultSecretOptions.TokenEnvRef"/> is unset — see
/// <c>ProductionSecretValidator</c>); this provider itself never reads
/// the env var. The in-process <see cref="IMemoryCache"/> fronts every
/// resolve with the TTL in <see cref="VaultSecretOptions.CacheTtl"/> so
/// Vault-side rotation picks up within one TTL without a restart
/// (issue #52 §Design — remote default 60s). Singleton: stateless
/// except for the shared cache + the shared <see cref="IVaultClient"/>.
/// <para>
/// When <see cref="VaultSecretOptions.Enabled"/> is false the provider
/// short-circuits to <c>null</c> (Option B from issue #52 §slice 2):
/// the DI factory still wires a (lazy-login) <see cref="IVaultClient"/>
/// in <c>HostComposer</c> with a placeholder token, but no Vault
/// round-trip ever happens, so a <c>vault:</c> reference in a disabled
/// deployment surfaces as a typed
/// <see cref="SecretRefUnsetException"/> (the composite resolver turns
/// the <c>null</c> into the typed error) rather than a 5xx. No wire
/// reference format here — refs are always <c>vault:path#key</c>; the
/// composite resolver handles routing and the parser already split the
/// ref into <see cref="SecretRef.Path"/> + <see cref="SecretRef.Key"/>.
/// </para>
/// </summary>
public sealed class VaultSecretProvider(
    IOptions<VaultSecretOptions> options,
    IVaultClient client,
    ILogger<VaultSecretProvider> logger,
    IMemoryCache cache) : ISecretProvider
{
    private static readonly ActivitySource activitySource = new("Comuki.Shared.Kernel");

    private static readonly Meter meter = new("comuki.secrets");

    /// <summary>Count of <see cref="ResolveAsync"/> outcomes, tagged by outcome (hit / miss / disabled).</summary>
    private static readonly Counter<long> resolutions = meter.CreateCounter<long>(
        "comuki.secrets.vault.resolved");

    /// <inheritdoc />
    public string Scheme => "vault";

    /// <inheritdoc />
    /// <remarks>
    /// Resolves a parsed reference via the VaultSharp K/V v2 engine. The
    /// composite resolver has already verified the scheme prefix; this
    /// provider asserts it defensively and throws
    /// <see cref="SecretRefFormatException"/> on a mismatch (mirrors the
    /// contract on <see cref="FileSecretProvider"/>). The cache front
    /// keeps the warm path a single in-memory lookup; cache miss falls
    /// through to VaultSharp, which uses the bootstrap token bound at
    /// <see cref="IVaultClient"/> construction.
    /// </remarks>
    /// <param name="reference">Parsed reference — <see cref="SecretRef.Scheme"/> == <c>vault</c>, <see cref="SecretRef.Path"/> is the K/V v2 secret path, <see cref="SecretRef.Key"/> is the field inside the secret.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="SecretRefFormatException">Reference scheme is not <c>vault</c>, or <see cref="SecretRef.Key"/> is null/empty (Vault K/V v2 refs always carry a field).</exception>
    public async Task<string?> ResolveAsync(SecretRef reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var optionsValue = options.Value;

        if (!optionsValue.Enabled)
        {
            resolutions.Add(1, new KeyValuePair<string, object?>("outcome", "disabled"));
            logger.LogDebug("Vault provider is disabled ({Scheme}); skipping resolve for {Path}", reference.Scheme, reference.Path);
            return null;
        }

        using var activity = activitySource.StartActivity("comuki.secrets.vault.resolve");

        if (!string.Equals(reference.Scheme, "vault", StringComparison.OrdinalIgnoreCase))
        {
            throw new SecretRefFormatException(
                $"vault provider received a reference with scheme '{reference.Scheme}'; expected 'vault'");
        }

        if (string.IsNullOrEmpty(reference.Key))
        {
            throw new SecretRefFormatException(
                $"vault reference '{reference.Path}' is missing the field key (use 'vault:{reference.Path}#field'); "
                + "the Vault K/V v2 engine resolves fields inside a secret, not the secret envelope itself");
        }

        var cacheKey = VaultSecretProviderHelpers.CacheKey(optionsValue.Address, optionsValue.KvMount, reference.Path, reference.Key);
        activity?.SetTag("vault.address", optionsValue.Address);
        activity?.SetTag("vault.mount", optionsValue.KvMount);
        activity?.SetTag("vault.path", reference.Path);
        activity?.SetTag("vault.key", reference.Key);

        if (cache.TryGetValue(cacheKey, out string? cached))
        {
            resolutions.Add(1, new KeyValuePair<string, object?>("outcome", "hit"));
            activity?.SetTag("outcome", "hit");
            return cached;
        }

        activity?.SetTag("outcome", "miss");
        resolutions.Add(1, new KeyValuePair<string, object?>("outcome", "miss"));

        var secret = await client.V1.Secrets.KeyValue.V2.ReadSecretAsync(
            path: reference.Path,
            mountPoint: optionsValue.KvMount);
        var value = VaultSecretProviderHelpers.ExtractFieldValue(secret, reference.Key);

        if (value is not null && optionsValue.CacheTtl > TimeSpan.Zero)
        {
            cache.Set(cacheKey, value, optionsValue.CacheTtl);
        }

        return value;
    }
}
