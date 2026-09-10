using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Options;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Proxy.Application.Resolving;

/// <summary>
/// Pure-logic builder for the lazy <see cref="VirtualKeySeed"/> snapshot.
/// Extracted from <see cref="VirtualKeySeed"/> so the seed class stays free
/// of <c>private static</c> helpers (per <c>class-layout-and-tooling.md</c>
/// §1a — no private methods in production). <c>internal</c> because only
/// <see cref="VirtualKeySeed"/> in this assembly consumes it. Each row's
/// API-key reference is resolved through the shared
/// <see cref="ISecretResolver"/> so a missing key surfaces a 502 at request
/// time rather than silently dropping the row.
/// </summary>
internal static class VirtualKeySeedBuilder
{
    /// <summary>
    /// Build the in-memory snapshot from <paramref name="snapshot"/>. Rows
    /// with an empty token or unset project id are dropped with a warning;
    /// rows whose API-key ref resolves empty land in the snapshot anyway
    /// (a 502 surfaces the missing value at request time — see the spec
    /// for <c>ProxyTransforms</c>). Pure async — no sync-on-async.
    /// </summary>
    /// <param name="snapshot">Bound <c>Proxy:*</c> configuration.</param>
    /// <param name="resolver">Shared-kernel resolver — routes by scheme to the matching provider.</param>
    /// <param name="logger">Structured logger; warns when a referenced ref resolves empty.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<IDictionary<string, VirtualKey>> BuildAsync(
        ProxyOptions snapshot,
        ISecretResolver resolver,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var index = new Dictionary<string, VirtualKey>(StringComparer.Ordinal);
        foreach (var config in snapshot.VirtualKeys ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(config.Token) || config.ProjectId == Guid.Empty)
            {
                logger.LogWarning("Skipping invalid virtual key configuration entry (token or project id missing)");
                continue;
            }

            var apiKey = await resolver.ResolveAsync(config.ApiKeyEnvRef, cancellationToken);
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
