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
/// restart picks up new keys.
/// </summary>
/// <param name="options">Bound <c>Proxy:*</c> configuration.</param>
/// <param name="logger">Structured logger; warns when a referenced env var is unset.</param>
public sealed class ConfigurationVirtualKeyStore(IOptions<ProxyOptions> options, ILogger<ConfigurationVirtualKeyStore> logger) : IVirtualKeyStore
{
    private readonly Lazy<IReadOnlyDictionary<string, VirtualKey>> byToken = new(() => BuildIndex(options.Value, logger));

    /// <inheritdoc />
    public Task<VirtualKey?> FindAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<VirtualKey?>(null);
        }

        byToken.Value.TryGetValue(token, out var match);
        return Task.FromResult(match);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VirtualKey>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<VirtualKey> snapshot = [.. byToken.Value.Values];
        return Task.FromResult(snapshot);
    }

    private static IReadOnlyDictionary<string, VirtualKey> BuildIndex(ProxyOptions snapshot, ILogger logger)
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
                AllowedModels: config.AllowedModels);
        }

        return index;
    }
}
