using Comuki.Modules.Observability.Application.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Modules.Observability.Application;

/// <summary>
/// Composition entry point for the Observability module's application
/// layer. The host composition calls <see cref="AddObservabilityApplication"/>
/// in the same chain that builds the typed clients; the method itself
/// binds the typed options so the infrastructure layer (which reads
/// them through <c>IOptions&lt;ObservabilityOptions&gt;</c>) resolves a
/// real instance. Validation stays in the host
/// (<c>ValidateDataAnnotations().ValidateOnStart()</c>) — the
/// application project has no IOptions host, only an options type.
/// </summary>
public static class ObservabilityApplicationExtensions
{
    /// <summary>
    /// Binds <see cref="ObservabilityOptions"/> on the
    /// <see cref="ObservabilityOptions.SectionName"/> configuration
    /// section so the infrastructure Refit clients can resolve the
    /// base URLs through <c>IOptions&lt;ObservabilityOptions&gt;</c>.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddObservabilityApplication(this IServiceCollection services)
    {
        services.AddOptions<ObservabilityOptions>();
        return services;
    }
}
