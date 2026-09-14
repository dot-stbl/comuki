using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Shared.Bootstrap;

/// <summary>Registration entry point for the comuki worker registry.</summary>
public static class ComukiWorkerRegistryExtensions
{
    /// <summary>
    /// Registers the <see cref="ComukiWorkerRegistry"/> — the single
    /// hosted service that drives every <see cref="IComukiWorker"/>
    /// registration (interval + startup schedules, per-cycle scopes,
    /// exponential backoff, status snapshot). Call once per host after
    /// the module installers registered their workers.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    public static IServiceCollection AddComukiWorkers(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ComukiWorkerRegistry>();
        services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<ComukiWorkerRegistry>());

        return services;
    }
}
