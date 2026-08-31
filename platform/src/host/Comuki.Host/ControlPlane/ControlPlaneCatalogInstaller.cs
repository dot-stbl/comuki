using Comuki.Shared.Contracts.ControlPlane.ChatCommands;
using Comuki.Shared.Contracts.ControlPlane.Profiles;

namespace Comuki.Host.ControlPlane;

/// <summary>Registration entry point for the control-plane content catalog.</summary>
public static class ControlPlaneCatalogInstaller
{
    /// <summary>
    /// Registers <see cref="ControlPlaneCatalog"/> as a singleton and binds
    /// both catalog ports to the same instance, so interface and concrete
    /// resolutions share one catalog. Options come from the
    /// <c>ControlPlane</c> section (<see cref="ControlPlaneCatalogOptions"/>).
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    public static IServiceCollection AddControlPlaneCatalogCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = services.AddOptions<ControlPlaneCatalogOptions>()
            .Bind(configuration.GetSection(ControlPlaneCatalogOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = services.AddSingleton<ControlPlaneCatalog>();
        _ = services.AddSingleton<IProfileCatalog>(static serviceProvider =>
            serviceProvider.GetRequiredService<ControlPlaneCatalog>());
        _ = services.AddSingleton<IChatCommandCatalog>(static serviceProvider =>
            serviceProvider.GetRequiredService<ControlPlaneCatalog>());

        return services;
    }
}
