using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Comuki.Shared.Bootstrap.Workers;
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
    /// opens its own context) and the ephemeral sweep worker. The sweep
    /// registers as an <see cref="IComukiWorker"/>; a host that runs it
    /// must also call <c>AddComukiWorkers()</c> (the orchestrator host and
    /// the brain host both do). Null-logger
    /// fallbacks keep the module resolvable outside a full host; a host
    /// that already registered logging wins (TryAdd). No
    /// <c>ISubjectScopeAccessor</c> registration is added here on purpose:
    /// <see cref="MemoryDbContext"/>, <see cref="EfMemoryStore"/> and
    /// <see cref="MemorySweepComukiWorker"/> all take it optionally and default
    /// to an unrestricted (system) view when none is registered — a host
    /// that cares about scoping (<c>Comuki.Host.Brain</c>) registers the
    /// real <c>AsyncLocalSubjectScopeAccessor</c> itself before calling
    /// this method.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    public static IServiceCollection AddMemoryPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<MemoryDbContext>(options =>
            MemoryDbContext.ApplyOptions(options, connectionString));

        services.TryAddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMemoryStore, EfMemoryStore>();
        services.AddSingleton<IComukiWorker, MemorySweepComukiWorker>();

        return services;
    }
}
