namespace Comuki.Host.Errors.Handlers.Intake;

/// <summary>
/// Registers the Intake module's domain-error rows into the typed
/// problem-handler registry — the replacement for the deleted
/// retired <c>Intake</c> runner catch arms, in the same shape the module
/// always shipped.
/// </summary>
public static class IntakeProblemHandlerRegistration
{
    /// <summary>Registers the intake not-found family, the secret-ref 400 and the claim-conflict 409 rows.</summary>
    /// <param name="services">Host service collection.</param>
    /// <returns>The collection, for chaining.</returns>
    public static IServiceCollection AddIntakeProblemHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IProblemHandler, IntakeTicketNotFoundProblemHandler>();
        services.AddSingleton<IProblemHandler, SourceConnectionNotFoundProblemHandler>();
        services.AddSingleton<IProblemHandler, AdmissionRuleNotFoundProblemHandler>();
        services.AddSingleton<IProblemHandler, SecretRefUnsetProblemHandler>();
        services.AddSingleton<IProblemHandler, IntakeTicketConflictProblemHandler>();

        return services;
    }
}
