namespace Comuki.Host.Errors.Handlers.Projects;

/// <summary>
/// Registers the Projects module's domain-error rows into the typed
/// problem-handler registry — the replacement for the deleted
/// retired <c>Projects</c> runner catch arms, in the same shape the module
/// always shipped.
/// </summary>
public static class ProjectsProblemHandlerRegistration
{
    /// <summary>Registers <c>ProjectNotFound</c> / <c>ProjectSettingsConflict</c> / <c>ProjectConflict</c> rows.</summary>
    /// <param name="services">Host service collection.</param>
    /// <returns>The collection, for chaining.</returns>
    public static IServiceCollection AddProjectsProblemHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IProblemHandler, ProjectNotFoundProblemHandler>();
        services.AddSingleton<IProblemHandler, ProjectSettingsConflictProblemHandler>();
        services.AddSingleton<IProblemHandler, ProjectConflictProblemHandler>();

        return services;
    }
}
