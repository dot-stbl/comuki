namespace Comuki.Host.Errors.Handlers.Learning;

/// <summary>
/// Registers the Learning surface's domain-error rows into the typed
/// problem-handler registry — the replacement for the deleted
/// retired <c>Learning</c> runner catch arm, in the same shape the surface
/// always shipped.
/// </summary>
public static class LearningProblemHandlerRegistration
{
    /// <summary>Registers the <c>LearningDecisionConflict</c> 409 row.</summary>
    /// <param name="services">Host service collection.</param>
    /// <returns>The collection, for chaining.</returns>
    public static IServiceCollection AddLearningProblemHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IProblemHandler, LearningDecisionConflictProblemHandler>();

        return services;
    }
}
