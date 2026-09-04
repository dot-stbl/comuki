using Comuki.Modules.Proxy.Application.Budgeting;
using Comuki.Modules.Proxy.Application.Extraction;
using Comuki.Modules.Proxy.Application.Metering;
using Comuki.Modules.Proxy.Application.Options;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Modules.Proxy.Application.Resolving;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Proxy.Application;

/// <summary>
/// Composition of the Proxy application layer: virtual-key store,
/// resolver, budget enforcer, pricing calculator, per-provider usage
/// extractors and the meter that hands reports to the costs module. The
/// <see cref="AddProxyApplication"/> extension wires everything that is
/// provider-agnostic; the YARP-side wiring lives in
/// <see cref="Infrastructure.ProxyInfrastructureExtensions"/>.
/// </summary>
public static class ProxyApplicationExtensions
{
    /// <summary>
    /// Registers the proxy application services. Bound options are
    /// validated via <c>ValidateDataAnnotations().ValidateOnStart()</c>
    /// so a missing token, empty base URL or absent env-var reference
    /// fail the host boot, not the first request.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configuration">Bound <c>Proxy:*</c> section.</param>
    public static IServiceCollection AddProxyApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<ProxyOptions>()
            .Bind(configuration.GetSection(ProxyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ProxyOptions>, ProxyOptionsValidator>();

        services.TryAddSingleton<IVirtualKeyStore, ConfigurationVirtualKeyStore>();
        services.AddSingleton<VirtualKeyResolver>();
        services.AddSingleton<IProxyBudgetEnforcer, DefaultProxyBudgetEnforcer>();
        services.AddSingleton<ProxyPricingCalculator>();
        services.AddSingleton<ProxyUsageMeter>();

        services.AddSingleton<IProxyUsageExtractor, OpenAiUsageExtractor>();
        services.AddSingleton<IProxyUsageExtractor, AnthropicUsageExtractor>();

        return services;
    }
}
