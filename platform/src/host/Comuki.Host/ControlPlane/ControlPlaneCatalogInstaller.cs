using Comuki.Shared.Contracts.ControlPlane.ChatCommands;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Comuki.Shared.Contracts.ControlPlane.Skills;

namespace Comuki.Host.ControlPlane;

/// <summary>Registration entry point for the control-plane content catalog.</summary>
public static class ControlPlaneCatalogInstaller
{
    /// <summary>
    /// Registers <see cref="ControlPlaneCatalog"/>, <see cref="SkillCatalog"/>,
    /// and <see cref="ISkillCatalog"/> as singletons, plus the profile and
    /// chat-command catalog ports. All three readers share the
    /// <c>ControlPlane</c> options section (<see cref="ControlPlaneCatalogOptions"/>).
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    public static IServiceCollection AddControlPlaneCatalogCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ControlPlaneCatalogOptions>()
            .Bind(configuration.GetSection(ControlPlaneCatalogOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ControlPlaneCatalog>();
        services.AddSingleton<IProfileCatalog>(static serviceProvider =>
            serviceProvider.GetRequiredService<ControlPlaneCatalog>());
        services.AddSingleton<IChatCommandCatalog>(static serviceProvider =>
            serviceProvider.GetRequiredService<ControlPlaneCatalog>());

        services.AddSingleton<SkillCatalog>();
        services.AddSingleton<ISkillCatalog>(static serviceProvider =>
            serviceProvider.GetRequiredService<SkillCatalog>());

        return services;
    }
}
