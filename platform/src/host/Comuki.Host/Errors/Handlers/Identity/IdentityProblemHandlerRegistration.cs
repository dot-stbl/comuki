namespace Comuki.Host.Errors.Handlers.Identity;

/// <summary>
/// Registers the identity-admin surface's domain-error rows into the typed
/// problem-handler registry — the replacement for the deleted
/// <c>UsersController</c> catch arm, in the same shape the surface always
/// shipped.
/// </summary>
public static class IdentityProblemHandlerRegistration
{
    /// <summary>Registers the <c>OidcLinkConflict</c> 409 row.</summary>
    /// <param name="services">Host service collection.</param>
    /// <returns>The collection, for chaining.</returns>
    public static IServiceCollection AddIdentityProblemHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IProblemHandler, OidcLinkConflictProblemHandler>();

        return services;
    }
}
