using Comuki.Modules.Scheduler.Application.Ports;
using Comuki.Modules.Scheduler.Infrastructure.Persistence;
using Comuki.Modules.Scheduler.Infrastructure.Persistence.Stores;
using Comuki.Modules.Scheduler.Infrastructure.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Scheduler.Infrastructure;

/// <summary>Registration entry point for Scheduler persistence + the dispatcher hosted service.</summary>
public static class SchedulerPersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="SchedulerDbContext"/> (Npgsql + snake_case +
    /// private migrations history via <see cref="SchedulerDbContext.ApplyOptions"/>),
    /// the scheduled-job store (scoped — one context per unit of work)
    /// and the dispatcher worker as a hosted service.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString"></param>
    /// <returns></returns>
    public static IServiceCollection AddSchedulerPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<SchedulerDbContext>(options =>
            SchedulerDbContext.ApplyOptions(options, connectionString));

        services.AddScoped<IScheduledJobStore, ScheduledJobStore>();
        services.AddSingleton<ScheduledJobDispatcherWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ScheduledJobDispatcherWorker>());

        return services;
    }
}
