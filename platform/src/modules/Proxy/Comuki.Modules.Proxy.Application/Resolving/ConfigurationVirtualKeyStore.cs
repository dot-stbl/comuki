using System.Collections.Concurrent;
using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Proxy.Application.Resolving;

/// <summary>
/// Reads virtual keys from <see cref="Options.ProxyOptions"/> at startup, resolves
/// the upstream API-key reference through <see cref="Shared.Kernel.Secrets.ISecretResolver"/>,
/// and exposes the snapshot through <see cref="IVirtualKeyStore"/>. Runtime mints
/// (issue #122) land in a separate in-memory overlay beside the config-seeded keys —
/// both resolve identically through <see cref="IVirtualKeyStore.FindAsync"/>, so the
/// auth path treats a minted key exactly like a configured one.
/// Hot-reload is out of scope for v1 — a restart picks up new keys.
/// Removed keys live on for a short grace period (Q31 — 60s default) so
/// an operator deleting a key mid-call does not produce a 401 for the
/// in-flight request; minted keys are exempt from the grace window — a
/// revoked mint must fail authentication immediately because it marks a
/// terminal work item. The seed step runs lazily on the first
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

    /// <summary>Runtime-minted keys (issue #122) — checked before the config-seeded set.</summary>
    private readonly ConcurrentDictionary<string, VirtualKey> minted = new(StringComparer.Ordinal);

    /// <summary>Recently-deleted keys (see <see cref="GraceEntry"/>) — answerable until the grace expiry.</summary>
    private readonly ConcurrentDictionary<string, GraceEntry> grace = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<VirtualKey?> FindAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        if (minted.TryGetValue(token, out var mintedKey))
        {
            return mintedKey;
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
        return [.. byToken.Values, .. minted.Values];
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        // A minted key is revoked by a terminal work-item path, not by an
        // operator: no grace window — the very next request with it must 401.
        if (minted.TryRemove(token, out var revoked))
        {
            logger.LogInformation(
                "Minted virtual key {TokenPrefix} of work item {WorkItemId} revoked",
                token[..Math.Min(8, token.Length)],
                revoked.WorkItemId);
            return;
        }

        await seed.EnsureAppliedAsync(byToken, cancellationToken);

        if (byToken.TryRemove(token, out var removed))
        {
            var entry = new GraceEntry(removed, clock.GetUtcNow() + DefaultGracePeriod);
            grace[token] = entry;
            logger.LogInformation(
                "Virtual key {TokenPrefix} moved to the deletion grace window until {ExpiresAt:o}",
                token[..Math.Min(8, token.Length)],
                entry.ExpiresAt);
        }
    }

    /// <inheritdoc />
    public async Task MintAsync(
        string token,
        ProjectId projectId,
        Guid workItemId,
        DateTimeOffset expiresAt,
        IReadOnlyList<string>? allowedModels = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        // The seed must be applied first: the mint inherits its upstream
        // from the configured provider keys in byToken.
        await seed.EnsureAppliedAsync(byToken, cancellationToken);
        minted[token] = new VirtualKey(
            Token: token,
            ProjectId: projectId,
            Upstream: MintedKeyUpstream.Select(byToken.Values, projectId),
            BudgetUsd: null,
            ExpiresAt: expiresAt,
            AllowedModels: allowedModels,
            WorkItemId: workItemId);
    }
}
