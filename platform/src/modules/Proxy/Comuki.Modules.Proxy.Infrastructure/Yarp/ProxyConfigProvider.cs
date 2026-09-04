using Comuki.Modules.Proxy.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Comuki.Modules.Proxy.Infrastructure.Yarp;

/// <summary>
/// Wraps <see cref="InMemoryConfigProvider"/> and rebuilds the
/// configuration snapshot when <see cref="IOptionsMonitor{T}.OnChange"/>
/// fires. One route per provider cluster (<c>/v1/chat/completions</c> →
/// <c>openai</c>, <c>/v1/messages</c> → <c>anthropic</c>); custom providers
/// get their own route when a virtual key uses them. The
/// <see cref="Auth.VirtualKeyAuthenticationHandler"/> store is not
/// flushed on option reload — restart picks up new keys for v1.
/// </summary>
/// <param name="options">Bound proxy options.</param>
/// <param name="logger">Structured logger.</param>
public sealed class ProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private readonly InMemoryConfigProvider inner;
    private readonly IDisposable changeSubscription = null!;

    /// <summary>Constructs the provider with the initial snapshot.</summary>
    public ProxyConfigProvider(IOptionsMonitor<ProxyOptions> options, ILogger<ProxyConfigProvider> logger)
    {
        var (routes, clusters) = BuildConfig(options.CurrentValue, logger);
        inner = new InMemoryConfigProvider(routes, clusters);

        var subscription = options.OnChange((snapshot, _) =>
        {
            logger.LogInformation("Rebuilding YARP proxy config after Proxy options change");
            var (newRoutes, newClusters) = BuildConfig(snapshot, logger);
            inner.Update(newRoutes, newClusters);
        });

        if (subscription is not null)
        {
            changeSubscription = subscription;
        }
    }

    /// <inheritdoc />
    public IProxyConfig GetConfig()
    {
        return inner.GetConfig();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        changeSubscription.Dispose();
    }

    private static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) BuildConfig(ProxyOptions snapshot, ILogger logger)
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

        return (routes, clusters);
    }

    private static string ProviderRoute(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "openai" => "/v1/chat/completions",
            "anthropic" => "/v1/messages",
            _ => $"/v1/{provider.ToLowerInvariant()}/chat/completions",
        };
    }
}
