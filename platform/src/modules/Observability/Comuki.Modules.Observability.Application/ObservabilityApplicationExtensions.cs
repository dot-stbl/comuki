using Comuki.Modules.Observability.Application.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Observability.Application;

/// <summary>
/// Registration extension for the Observability module application
/// layer. Currently empty — the application layer is the typed port
/// surface; the typed options bind here so <c>ValidateDataAnnotations</c>
/// + <c>ValidateOnStart</c> sees the <see cref="ObservabilityOptions"/>
/// rules without depending on the infrastructure layer. Composition
/// roots compose the module with <c>AddObservabilityInfrastructure</c>
/// from the infrastructure project.
/// </summary>
public static class ObservabilityApplicationExtensions
{
    /// <summary>Registers the observability typed options binding.</summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddObservabilityApplication(this IServiceCollection services)
    {
        // The options binding lives in the application layer so the
        // infrastructure Refit clients can read it via IOptions without
        // dragging in the application layer's other types — the port
        // types compile against the Domain project only.
        services.AddOptions<ObservabilityOptions>();
        return services;
    }
}
