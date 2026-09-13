using Comuki.Modules.Proxy.Application.Options;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Comuki.Modules.Proxy.Infrastructure.Yarp;

/// <summary>
/// Derived routes + clusters for one <c>Proxy:VirtualKeys</c> snapshot —
/// built purely from the options, no provider state.
/// </summary>
/// <param name="Routes">One route per distinct provider with a virtual key.</param>
/// <param name="Clusters">One cluster per provider that carries a base URL on some virtual key.</param>
internal sealed record ProxyConfig(IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters);

/// <summary>
/// Pure derivation of the YARP route / cluster snapshot from
/// <c>ProxyOptions.VirtualKeys</c> — isolated so the provider class holds
/// only the change-subscription plumbing (<c>code-shape.md</c> §1a).
/// </summary>
internal static class ProxyConfigFactory
{
    /// <summary>Builds routes and clusters for the given options snapshot, skipping providers without a base URL (logged).</summary>
    /// <param name="snapshot">Current virtual-key options.</param>
    /// <param name="logger">Warns about providers whose virtual keys carry no base URL.</param>
    public static ProxyConfig Build(ProxyOptions snapshot, ILogger logger)
    {
        var routes = new List<RouteConfig>();
        var providers = snapshot.VirtualKeys
            .Select(key => key.Provider)
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var provider in providers)
        {
            var routeId = $"proxy-{provider.ToLowerInvariant()}";
            var clusterId = provider.ToLowerInvariant();
            var path = ProviderRoute(provider);

            routes.Add(new RouteConfig
            {
                RouteId = routeId,
                ClusterId = clusterId,
                Match = new RouteMatch { Path = path, Methods = ["POST"] },
            });
        }

        var clusters = new List<ClusterConfig>();
        foreach (var provider in providers)
        {
            var baseUrl = snapshot.VirtualKeys
                .FirstOrDefault(key => string.Equals(key.Provider, provider, StringComparison.OrdinalIgnoreCase))
                ?.BaseUrl;

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                logger.LogWarning("Skipping YARP cluster {Provider} — no virtual key carries a base URL", provider);
                continue;
            }

            clusters.Add(new ClusterConfig
            {
                ClusterId = provider.ToLowerInvariant(),
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    [provider.ToLowerInvariant()] = new DestinationConfig
                    {
                        Address = baseUrl.TrimEnd('/') + "/",
                    },
                },
            });
        }

        return new ProxyConfig(routes, clusters);
    }

    /// <summary>Request path one provider's route matches — the well-known API roots for openai / anthropic, the generic chat root otherwise.</summary>
    /// <param name="provider">Configured upstream provider id.</param>
    public static string ProviderRoute(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "openai" => "/v1/chat/completions",
            "anthropic" => "/v1/messages",
            _ => $"/v1/{provider.ToLowerInvariant()}/chat/completions",
        };
    }
}
