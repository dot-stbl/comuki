using Comuki.Modules.Costs.Application.Ports;
using Comuki.Modules.Costs.Infrastructure.Persistence;
using Comuki.Modules.Costs.Infrastructure.Persistence.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Comuki.Modules.Costs.Infrastructure;

/// <summary>Registration entry point for Costs persistence.</summary>
public static class CostsPersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="CostsDbContext"/> (Npgsql + snake_case + private
    /// migrations history) and the usage event store (singleton over the
    /// context factory).
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    public static IServiceCollection AddCostsPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<CostsDbContext>(options =>
            CostsDbContext.ApplyOptions(options, connectionString));

        services.TryAddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IUsageEventStore, EfUsageEventStore>();

        return services;
    }
}
