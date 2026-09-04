using Comuki.Modules.Proxy.Infrastructure.Auth;
using Comuki.Modules.Proxy.Infrastructure.Yarp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace Comuki.Modules.Proxy.Infrastructure;

/// <summary>
/// Wires the YARP config provider, the request / response transformers,
/// and the <see cref="VirtualKeyAuthenticationHandler"/> scheme. Call
/// after <see cref="Application.ProxyApplicationExtensions.AddProxyApplication"/>
/// and before <c>app.UseAuthentication()</c>.
/// </summary>
public static class ProxyInfrastructureExtensions
{
    /// <summary>
    /// Registers YARP services and the proxy config provider, then adds
    /// the virtual-key auth + upstream-key rewrite transforms to every
    /// route the <see cref="ProxyConfigProvider"/> emits. The
    /// <see cref="AddVirtualKeyAuth"/> call registers the scheme on the
    /// host's authentication configuration without resetting the cookie /
    /// API-key defaults the Identity module owns.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <returns>The same collection for fluent chaining.</returns>
    public static IServiceCollection AddProxyInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ProxyConfigProvider>();
        services.AddSingleton<IProxyConfigProvider>(static serviceProvider => serviceProvider.GetRequiredService<ProxyConfigProvider>());

        services.AddReverseProxy()
            .AddTransforms(static transformBuilderContext =>
            {
                transformBuilderContext.AddRequestTransform(ProxyTransforms.RewriteAuthFromVirtualKeyAsync);
                transformBuilderContext.AddResponseTransform(ProxyTransforms.MeterUsageFromResponseAsync);
            });

        services.AddVirtualKeyAuth();
        return services;
    }

    /// <summary>
    /// Registers the <see cref="VirtualKeyAuthenticationHandler"/> scheme
    /// on the existing authentication configuration. We chain onto the
    /// builder <see cref="AddAuthenticationCore"/> returned by the host
    /// so cookie / API-key schemes (Identity) and the proxy's bearer
    /// scheme all coexist; calling <see cref="AddAuthenticationCore"/>
    /// with the default options would otherwise reset the cookie scheme.
    /// </summary>
    /// <param name="services">DI container.</param>
    public static IServiceCollection AddVirtualKeyAuth(this IServiceCollection services)
    {
        _ = services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, VirtualKeyAuthenticationHandler>(
                VirtualKeyAuthenticationHandler.SchemeName,
                static _ => { });
        return services;
    }
}
