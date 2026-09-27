namespace Comuki.Host.Errors.Handlers.Runs;

/// <summary>
/// Registers the Runs surface's domain-error rows into the typed
/// problem-handler registry — the replacement for the deleted
/// retired <c>Runs</c> runner catch arms, in the same shape the surface
/// always shipped.
/// </summary>
public static class RunsProblemHandlerRegistration
{
    /// <summary>Registers the <c>RunDecisionConflict</c> 409 and <c>FilterParse</c> 400 rows.</summary>
    /// <param name="services">Host service collection.</param>
    /// <returns>The collection, for chaining.</returns>
    public static IServiceCollection AddRunsProblemHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IProblemHandler, RunDecisionConflictProblemHandler>();
        services.AddSingleton<IProblemHandler, FilterParseProblemHandler>();

        return services;
    }
}
