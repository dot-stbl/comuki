namespace Comuki.Host.Errors.Core;

/// <summary>
/// Registers the typed problem-handler mechanism: the
/// <see cref="ProblemHandlerRegistry"/> singleton that seeds the built-in
/// kernel rows and collects every module handler registered as
/// <see cref="IProblemHandler"/>. Called beside <c>AddExceptionHandler</c>
/// in the composition root — and by any test host that boots the central
/// handler, so the registry is always resolvable.
/// </summary>
public static class CoreProblemHandlersRegistration
{
    /// <summary>Registers the <see cref="ProblemHandlerRegistry"/> with its built-in kernel defaults.</summary>
    /// <param name="services">Host service collection.</param>
    /// <returns>The collection, for chaining.</returns>
    public static IServiceCollection AddCoreProblemHandlers(this IServiceCollection services)
    {
        return services.AddSingleton<ProblemHandlerRegistry>();
    }
}
