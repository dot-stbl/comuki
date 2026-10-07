using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Infrastructure.Persistence;
using Comuki.Modules.Integrations.Infrastructure.Persistence.Stores;
using Comuki.Modules.Integrations.Infrastructure.Sync;
using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Integrations.Infrastructure;

/// <summary>Registration entry point for Integrations persistence.</summary>
public static class IntegrationsPersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="IntegrationsDbContext"/> (Npgsql + snake_case +
    /// private migrations history via
    /// <see cref="IntegrationsDbContext.ApplyOptions"/>), the integrations store
    /// (scoped — one context per unit of work) and the run status bridge
    /// worker. The bridge registers as an <see cref="IComukiWorker"/>; a
    /// host that runs it must also call <c>AddComukiWorkers()</c>. The
    /// shared-kernel <c>ISecretResolver</c> is registered by
    /// the host composition root — there is no Integrations-local copy.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    /// <returns></returns>
    public static IServiceCollection AddIntegrationsPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<IntegrationsDbContext>(options =>
            IntegrationsDbContext.ApplyOptions(options, connectionString));

        services.AddScoped<IIntegrationsStore, IntegrationsStore>();
        services.AddSingleton<IComukiWorker, RunStatusBridgeComukiWorker>();

        return services;
    }
}
