using Comuki.Modules.Artifacts.Application.Packaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Comuki.Modules.Artifacts.Application;

/// <summary>
/// Composition of the Artifacts application layer: the packager service,
/// its <see cref="BackgroundService"/> driver and the bookkeeping ports it
/// uses. The default <see cref="IRunArtifactBundleStore"/> is the
/// no-op stub (always reports <em>not bundled</em>) — the host replaces it
/// with the EF-backed implementation over the <c>artifacts</c> schema via
/// <see cref="TryAddSingleton{TService,TImplementation}"/>.
/// </summary>
public static class ArtifactsApplicationExtensions
{
    /// <summary>
    /// Registers the packager port, its driver <see cref="BackgroundService"/>
    /// and a no-op bundle store. The bundle store and the artifact store are
    /// replaced by the host before <see cref="IHostedService.StartAsync"/>
    /// fires (no-op stubs satisfy the contract for tests that compose the
    /// module in isolation).
    /// </summary>
    /// <param name="services"></param>
    public static IServiceCollection AddArtifactsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRunArtifactBundleStore, NullRunArtifactBundleStore>();
        services.AddSingleton<RunArtifactPackager>();
        services.AddHostedService<RunArtifactPackagerService>();
        return services;
    }
}
