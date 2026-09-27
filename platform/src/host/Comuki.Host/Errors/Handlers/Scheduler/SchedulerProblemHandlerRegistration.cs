namespace Comuki.Host.Errors.Handlers.Scheduler;

/// <summary>
/// Registers the Scheduler module's domain-error rows into the typed
/// problem-handler registry — the replacement for the deleted
/// retired <c>Scheduler</c> runner catch arms, in the same shape the module
/// always shipped.
/// </summary>
public static class SchedulerProblemHandlerRegistration
{
    /// <summary>Registers the <c>ScheduledJobNotFound</c> 404 and <c>InvalidCronExpression</c> 400 rows.</summary>
    /// <param name="services">Host service collection.</param>
    /// <returns>The collection, for chaining.</returns>
    public static IServiceCollection AddSchedulerProblemHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IProblemHandler, ScheduledJobNotFoundProblemHandler>();
        services.AddSingleton<IProblemHandler, InvalidCronExpressionProblemHandler>();

        return services;
    }
}
