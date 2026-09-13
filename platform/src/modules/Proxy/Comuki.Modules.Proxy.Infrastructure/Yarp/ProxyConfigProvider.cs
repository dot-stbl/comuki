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
public sealed class ProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private readonly InMemoryConfigProvider inner;
    private readonly IDisposable? changeSubscription;

    /// <summary>Constructs the provider with the initial snapshot.</summary>
    /// <param name="options">Bound proxy options.</param>
    /// <param name="logger">Structured logger.</param>
    public ProxyConfigProvider(IOptionsMonitor<ProxyOptions> options, ILogger<ProxyConfigProvider> logger)
    {
        var config = ProxyConfigFactory.Build(options.CurrentValue, logger);
        inner = new InMemoryConfigProvider(config.Routes, config.Clusters);

        changeSubscription = options.OnChange((snapshot, _) =>
        {
            logger.LogInformation("Rebuilding YARP proxy config after Proxy options change");
            var rebuilt = ProxyConfigFactory.Build(snapshot, logger);
            inner.Update(rebuilt.Routes, rebuilt.Clusters);
        });
    }

    /// <inheritdoc />
    public IProxyConfig GetConfig()
    {
        return inner.GetConfig();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        changeSubscription?.Dispose();
    }
}
