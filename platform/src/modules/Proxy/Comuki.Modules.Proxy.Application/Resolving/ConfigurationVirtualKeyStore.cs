using System.Collections.Concurrent;
using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Proxy.Application.Resolving;

/// <summary>
/// Reads virtual keys from <see cref="Options.ProxyOptions"/> at startup, resolves
/// the upstream API-key reference through <see cref="Shared.Kernel.Secrets.ISecretResolver"/>,
/// and exposes the snapshot through <see cref="IVirtualKeyStore"/>.
/// Hot-reload is out of scope for v1 — a restart picks up new keys.
/// Removed keys live on for a short grace period (Q31 — 60s default) so
/// an operator deleting a key mid-call does not produce a 401 for the
/// in-flight request. The seed step runs lazily on the first
/// <see cref="FindAsync"/> / <see cref="ListAsync"/> call via
/// <see cref="VirtualKeySeed"/> (separate class per
/// <c>code-shape.md</c> §1a — no private methods in production).
/// </summary>
/// <param name="clock">Wall-clock for the grace window boundary.</param>
/// <param name="seed">Lazy seed step that builds the in-memory snapshot from options + resolver.</param>
/// <param name="logger">Structured logger; warns when a referenced ref resolves empty.</param>
public sealed class ConfigurationVirtualKeyStore(
    TimeProvider clock,
    VirtualKeySeed seed,
    ILogger<ConfigurationVirtualKeyStore> logger) : IVirtualKeyStore
{
    /// <summary>Default grace window for a deleted key (Q31).</summary>
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(60);

    /// <summary>Active virtual keys (populated by the seed step above).</summary>
    private readonly ConcurrentDictionary<string, VirtualKey> byToken = new(StringComparer.Ordinal);

    /// <summary>Recently-deleted keys — value = original key + UTC expiry of the grace window.</summary>
    private readonly ConcurrentDictionary<string, (VirtualKey Key, DateTimeOffset ExpiresAt)> grace = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<VirtualKey?> FindAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        await seed.EnsureAppliedAsync(byToken, cancellationToken);

        return byToken.TryGetValue(token, out var match)
            ? match
            : grace.TryGetValue(token, out var ghosted) && clock.GetUtcNow() < ghosted.ExpiresAt
                ? ghosted.Key
                : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VirtualKey>> ListAsync(CancellationToken cancellationToken = default)
    {
        await seed.EnsureAppliedAsync(byToken, cancellationToken);
        return [.. byToken.Values];
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        await seed.EnsureAppliedAsync(byToken, cancellationToken);

        if (byToken.TryRemove(token, out var removed))
        {
            grace[token] = (removed, clock.GetUtcNow() + DefaultGracePeriod);
            logger.LogInformation(
                "Virtual key {TokenPrefix} moved to the deletion grace window until {ExpiresAt:o}",
                token[..Math.Min(8, token.Length)],
                grace[token].ExpiresAt);
        }
    }
}
