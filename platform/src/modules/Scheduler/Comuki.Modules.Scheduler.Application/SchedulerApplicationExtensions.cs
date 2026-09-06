using Comuki.Modules.Scheduler.Application.Jobs;
using Comuki.Modules.Scheduler.Application.Options;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Modules.Scheduler.Application;

/// <summary>Registration entry point for the Scheduler module application layer.</summary>
public static class SchedulerApplicationExtensions
{
    /// <summary>
    /// Registers the service façade and the two validators. The
    /// <see cref="Ports.IScheduledJobStore"/> port is wired in
    /// <c>AddSchedulerPersistence</c> (Infrastructure); the
    /// <see cref="Ports.ISchedulerDispatcher"/> port is wired by the
    /// host (it knows the engine); the dispatcher worker lives in
    /// Infrastructure (it owns the BackgroundService dependency).
    /// </summary>
    /// <param name="services"></param>
    public static IServiceCollection AddSchedulerApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<ScheduledJobService>();
        services.AddSingleton<IValidator<CreateScheduledJobCommand>, CreateScheduledJobValidator>();
        services.AddSingleton<IValidator<UpdateScheduledJobCommand>, UpdateScheduledJobValidator>();

        services.AddOptions<SchedulerOptions>();

        return services;
    }
}
