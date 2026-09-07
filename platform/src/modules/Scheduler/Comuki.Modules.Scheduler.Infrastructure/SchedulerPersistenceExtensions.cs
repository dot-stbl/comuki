using Comuki.Modules.Scheduler.Application.Observers;
using Comuki.Modules.Scheduler.Application.Ports;
using Comuki.Modules.Scheduler.Infrastructure.Observers;
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
    /// the scheduled-job store (scoped — one context per unit of work),
    /// the dispatcher worker as a hosted service, and the two
    /// <see cref="ISchedulerObserver"/> implementations the dispatcher
    /// notifies after every fire.
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

        // Observers are singletons: the dispatcher resolves the whole
        // list once per cycle and invokes each. Registration order is
        // preserved by Microsoft.Extensions.DependencyInjection, so the
        // journal observer runs first (durable record), Sentry after
        // (best-effort side-channel).
        services.AddSingleton<ISchedulerObserver, JournalSchedulerObserver>();
        services.AddSingleton<ISchedulerObserver, SentrySchedulerObserver>();

        services.AddSingleton<ScheduledJobDispatcherWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ScheduledJobDispatcherWorker>());

        return services;
    }
}
