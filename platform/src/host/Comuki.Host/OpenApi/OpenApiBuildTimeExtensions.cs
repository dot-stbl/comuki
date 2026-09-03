using System.Reflection;

namespace Comuki.Host.OpenApi;

/// <summary>
///     Build-time OpenAPI document generation safety for issue #29.
///     <c>Microsoft.Extensions.ApiDescription.Server</c> launches this app's entry
///     point (as the <c>GetDocument.Insider</c> tool) purely to enumerate
///     endpoints, then stops the host — but since .NET 7 it starts every
///     <see cref="IHostedService" /> first. Without intervention a plain
///     <c>dotnet build</c> would run the orchestrator migrators/workers and
///     execute business logic (including DB I/O) just to emit
///     <c>artifacts/openapi.json</c>.
/// </summary>
/// <remarks>
///     Ref: Microsoft Learn — "Customize runtime behavior during build-time
///     document generation" (<c>aspnetcore/fundamentals/openapi</c>).
/// </remarks>
internal static class OpenApiBuildTimeExtensions
{
    private const string DocumentToolAssemblyName = "GetDocument.Insider";

    /// <summary>
    ///     <see langword="true" /> when the current process is the build-time OpenAPI
    ///     document generator (the <c>GetDocument.Insider</c> tool), not a normal host run.
    /// </summary>
    public static bool IsOpenApiDocumentGeneration { get; } = string.Equals(
        Assembly.GetEntryAssembly()?.GetName().Name,
        DocumentToolAssemblyName,
        StringComparison.Ordinal);

    /// <summary>
    ///     Removes the application's own (<c>Comuki.*</c>) hosted services when running
    ///     under build-time OpenAPI generation, so no migrator/worker executes during a plain
    ///     <c>dotnet build</c>. The framework's web-host service is left intact so document
    ///     capture is unchanged. No-op at runtime (returns <paramref name="services" /> as-is).
    /// </summary>
    /// <param name="services">The service collection to strip hosted services from.</param>
    public static IServiceCollection RemoveHostedServicesForOpenApiGeneration(this IServiceCollection services)
    {
        if (!IsOpenApiDocumentGeneration)
        {
            return services;
        }

        var hostedServices = services
            .Where(static descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType?.Namespace?.StartsWith("Comuki.", StringComparison.Ordinal) == true)
            .ToList();

        foreach (var hostedService in hostedServices)
        {
            services.Remove(hostedService);
        }

        return services;
    }
}
