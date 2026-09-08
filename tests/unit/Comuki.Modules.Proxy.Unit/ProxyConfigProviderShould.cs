using Comuki.Modules.Proxy.Application.Options;
using Comuki.Modules.Proxy.Infrastructure.Yarp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>
/// YARP snapshot built from <see cref="ProxyOptions"/>: one route per
/// non-empty provider, one cluster per provider with a non-empty base
/// URL. Empty / whitespace provider and base-URL entries are filtered.
/// </summary>
public sealed class ProxyConfigProviderShould
{
    [Fact(DisplayName = "Given no virtual keys, when GetConfig runs, then the snapshot has no routes and no clusters")]
    public void EmptyStoreYieldsEmptyConfig()
    {
        var provider = NewProvider(NewOptions(VirtualKeys: []));

        var config = provider.GetConfig();

        config.Routes.ShouldBeEmpty();
        config.Clusters.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a single virtual key, when GetConfig runs, then one route and one cluster are built and the path matches the provider")]
    public void SingleKeyBuildsOneRouteAndCluster()
    {
        var provider = NewProvider(NewOptions(VirtualKeys:
        [
            NewKey(provider: "openai", baseUrl: "https://api.openai.com"),
        ]));

        var config = provider.GetConfig();

        config.Routes.Count.ShouldBe(1);
        config.Routes[0].RouteId.ShouldBe("proxy-openai");
        config.Routes[0].ClusterId.ShouldBe("openai");
        config.Routes[0].Match!.Path.ShouldBe("/v1/chat/completions");
        config.Routes[0].Match.Methods.ShouldBe(["POST"]);
        config.Clusters.Count.ShouldBe(1);
        config.Clusters[0]!.ClusterId.ShouldBe("openai");
        var destinations = config.Clusters[0]!.Destinations!;
        destinations.Keys.ShouldBe(["openai"]);
        destinations["openai"].Address.ShouldBe("https://api.openai.com/");
    }

    [Fact(DisplayName = "Given two virtual keys for different providers, when GetConfig runs, then both have routes and clusters with their own paths")]
    public void MultipleProvidersBuildDistinctRoutes()
    {
        var provider = NewProvider(NewOptions(VirtualKeys:
        [
            NewKey(provider: "openai", baseUrl: "https://api.openai.com"),
            NewKey(provider: "anthropic", baseUrl: "https://api.anthropic.com"),
        ]));

        var config = provider.GetConfig();

        config.Routes.Count.ShouldBe(2);
        config.Clusters.Count.ShouldBe(2);
        config.Routes.Select(static route => route.Match.Path).ShouldBe(["/v1/chat/completions", "/v1/messages"]);
        config.Clusters.Select(static cluster => cluster.ClusterId)
            .ShouldBe(["openai", "anthropic"]);
    }

    [Fact(DisplayName = "Given a virtual key with an empty provider or base URL, when GetConfig runs, then its route/cluster is excluded from the snapshot")]
    public void DisabledKeyIsExcluded()
    {
        var provider = NewProvider(NewOptions(VirtualKeys:
        [
            NewKey(provider: "openai", baseUrl: "https://api.openai.com"),
            NewKey(provider: string.Empty, baseUrl: "https://api.disabled.com"),
            NewKey(provider: "anthropic", baseUrl: "https://api.anthropic.com"),
        ]));

        var config = provider.GetConfig();

        config.Routes.Count.ShouldBe(2);
        config.Routes.Select(static route => route.RouteId).ShouldBe(["proxy-openai", "proxy-anthropic"]);
        config.Clusters.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Given a virtual key with an empty base URL, when GetConfig runs, then its cluster is skipped but the route still exists")]
    public void EmptyBaseUrlSkipsCluster()
    {
        var provider = NewProvider(NewOptions(VirtualKeys:
        [
            NewKey(provider: "openai", baseUrl: "https://api.openai.com"),
            NewKey(provider: "broken", baseUrl: string.Empty),
        ]));

        var config = provider.GetConfig();

        config.Routes.Count.ShouldBe(2);
        config.Clusters.Count.ShouldBe(1);
        config.Clusters[0].ClusterId.ShouldBe("openai");
    }

    [Fact(DisplayName = "Given two virtual keys for the same provider, when GetConfig runs, then the route is built once (Distinct by provider)")]
    public void DuplicateProviderCollapsesToOneRoute()
    {
        var provider = NewProvider(NewOptions(VirtualKeys:
        [
            NewKey(provider: "openai", baseUrl: "https://api.openai.com", projectId: Guid.NewGuid()),
            NewKey(provider: "openai", baseUrl: "https://api.openai.com", projectId: Guid.NewGuid()),
        ]));

        var config = provider.GetConfig();

        config.Routes.Count.ShouldBe(1);
        config.Clusters.Count.ShouldBe(1);
    }

    private static ProxyOptions NewOptions(IReadOnlyList<ProxyOptions.VirtualKeyConfiguration> VirtualKeys)
    {
        return new ProxyOptions
        {
            Enabled = true,
            VirtualKeys = VirtualKeys,
        };
    }

    private static ProxyOptions.VirtualKeyConfiguration NewKey(string provider, string baseUrl, Guid? projectId = null)
    {
        return new ProxyOptions.VirtualKeyConfiguration
        {
            Token = "vk_" + Guid.NewGuid().ToString("N").PadRight(16, 'x')[..32],
            ProjectId = projectId ?? Guid.NewGuid(),
            Provider = provider,
            BaseUrl = baseUrl,
            ApiKeyEnvRef = "TEST_API_KEY",
        };
    }

    private static ProxyConfigProvider NewProvider(ProxyOptions snapshot)
    {
        var monitor = Substitute.For<IOptionsMonitor<ProxyOptions>>();
        monitor.CurrentValue.Returns(snapshot);
        return new ProxyConfigProvider(monitor, NullLogger<ProxyConfigProvider>.Instance);
    }
}
