using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Comuki.Modules.Memory.Infrastructure;

/// <summary>Registration entry point for Memory persistence.</summary>
public static class MemoryPersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="MemoryDbContext"/> (Npgsql + snake_case +
    /// private migrations history via <see cref="MemoryDbContext.ApplyOptions"/>),
    /// the memory store (singleton over the context factory — every method
    /// opens its own context) and the ephemeral sweep worker. Null-logger
    /// fallbacks keep the module resolvable outside a full host; a host
    /// that already registered logging wins (TryAdd).
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    public static IServiceCollection AddMemoryPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        _ = services.AddDbContextFactory<MemoryDbContext>(options =>
            MemoryDbContext.ApplyOptions(options, connectionString));

        services.TryAddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.TryAddSingleton(TimeProvider.System);
        _ = services.AddSingleton<IMemoryStore, EfMemoryStore>();
        _ = services.AddHostedService<MemorySweepWorker>();

        return services;
    }
}
