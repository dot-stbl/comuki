using System.Collections.Concurrent;
using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Options;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Proxy.Application.Resolving;

/// <summary>
/// Reads virtual keys from <see cref="ProxyOptions"/> at startup, resolves
/// env-var references for upstream API keys and exposes the snapshot through
/// <see cref="IVirtualKeyStore"/>. Hot-reload is out of scope for v1 — a
/// restart picks up new keys. Removed keys live on for a short grace
/// period (Q31 — 60s default) so an operator deleting a key mid-call
/// does not produce a 401 for the in-flight request.
/// </summary>
/// <param name="options">Bound <c>Proxy:*</c> configuration.</param>
/// <param name="clock">Wall-clock for the grace window boundary.</param>
/// <param name="logger">Structured logger; warns when a referenced env var is unset.</param>
public sealed class ConfigurationVirtualKeyStore(
    IOptions<ProxyOptions> options,
    TimeProvider clock,
    ILogger<ConfigurationVirtualKeyStore> logger) : IVirtualKeyStore
{
    /// <summary>Default grace window for a deleted key (Q31).</summary>
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(60);

    /// <summary>Active virtual keys (seeded once from <see cref="ProxyOptions"/>).</summary>
    private readonly ConcurrentDictionary<string, VirtualKey> byToken = new(BuildSeed(options.Value, logger), StringComparer.Ordinal);

    /// <summary>Recently-deleted keys — value = original key + UTC expiry of the grace window.</summary>
    private readonly ConcurrentDictionary<string, (VirtualKey Key, DateTimeOffset ExpiresAt)> grace = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<VirtualKey?> FindAsync(string token, CancellationToken cancellationToken = default)
    {
        return string.IsNullOrWhiteSpace(token)
            ? Task.FromResult<VirtualKey?>(null)
            : byToken.TryGetValue(token, out var match)
            ? Task.FromResult<VirtualKey?>(match)
            : grace.TryGetValue(token, out var ghosted) && clock.GetUtcNow() < ghosted.ExpiresAt
            ? Task.FromResult<VirtualKey?>(ghosted.Key)
            : Task.FromResult<VirtualKey?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VirtualKey>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<VirtualKey> snapshot = [.. byToken.Values];
        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task RemoveAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.CompletedTask;
        }

        if (byToken.TryRemove(token, out var removed))
        {
            grace[token] = (removed, clock.GetUtcNow() + DefaultGracePeriod);
            logger.LogInformation(
                "Virtual key {TokenPrefix} moved to the deletion grace window until {ExpiresAt:o}",
                token[..Math.Min(8, token.Length)],
                grace[token].ExpiresAt);
        }

        return Task.CompletedTask;
    }

    private static IDictionary<string, VirtualKey> BuildSeed(ProxyOptions snapshot, ILogger logger)
    {
        var index = new Dictionary<string, VirtualKey>(StringComparer.Ordinal);
        foreach (var config in snapshot.VirtualKeys ?? [])
        {
            if (string.IsNullOrWhiteSpace(config.Token) || config.ProjectId == Guid.Empty)
            {
                logger.LogWarning("Skipping invalid virtual key configuration entry (token or project id missing)");
                continue;
            }

            var apiKey = Environment.GetEnvironmentVariable(config.ApiKeyEnvRef);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogWarning(
                    "Virtual key for project {ProjectId} references env var {EnvRef} which is unset; key will be rejected at request time",
                    config.ProjectId,
                    config.ApiKeyEnvRef);
            }

            index[config.Token] = new VirtualKey(
                Token: config.Token,
                ProjectId: new ProjectId(config.ProjectId),
                Upstream: new UpstreamSpec(
                    Provider: config.Provider,
                    BaseUrl: config.BaseUrl,
                    ApiKeyEnvRef: config.ApiKeyEnvRef,
                    DefaultModel: config.DefaultModel),
                BudgetUsd: config.BudgetUsd,
                ExpiresAt: config.ExpiresAt,
                AllowedModels: config.AllowedModels,
                MaxInputTokens: config.MaxInputTokens,
                MaxOutputTokens: config.MaxOutputTokens);
        }

        return index;
    }
}
