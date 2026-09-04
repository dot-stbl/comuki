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
    /// route the <see cref="ProxyConfigProvider"/> emits.
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

        return services;
    }

    /// <summary>
    /// Adds the <see cref="VirtualKeyAuthenticationHandler"/> scheme to
    /// the existing authentication builder. The scheme name
    /// (<see cref="VirtualKeyAuthenticationHandler.SchemeName"/>) is the
    /// identifier ASP.NET Core uses for challenge / forbid.
    /// </summary>
    /// <param name="builder">Authentication builder from <c>AddAuthentication(...)</c>.</param>
    public static AuthenticationBuilder AddVirtualKeyAuth(this AuthenticationBuilder builder)
    {
        return builder.AddScheme<AuthenticationSchemeOptions, VirtualKeyAuthenticationHandler>(
            VirtualKeyAuthenticationHandler.SchemeName,
            static _ => { });
    }
}
