namespace Comuki.Host.Errors.Handlers.Integration;

/// <summary>
/// Registers the Integrations module's domain-error rows into the typed
/// problem-handler registry — replaces the retired Intake runner catch
/// arms, in the same shape the module always shipped.
/// </summary>
public static class IntegrationProblemHandlerRegistration
{
    /// <summary>Registers the inbound-item not-found family, the secret-ref 400 and the claim-conflict 409 rows.</summary>
    /// <param name="services">Host service collection.</param>
    /// <returns>The collection, for chaining.</returns>
    public static IServiceCollection AddIntegrationProblemHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IProblemHandler, InboundItemNotFoundProblemHandler>();
        services.AddSingleton<IProblemHandler, SourceConnectionNotFoundProblemHandler>();
        services.AddSingleton<IProblemHandler, AdmissionRuleNotFoundProblemHandler>();
        services.AddSingleton<IProblemHandler, SecretRefUnsetProblemHandler>();
        services.AddSingleton<IProblemHandler, InboundItemConflictProblemHandler>();

        return services;
    }
}
