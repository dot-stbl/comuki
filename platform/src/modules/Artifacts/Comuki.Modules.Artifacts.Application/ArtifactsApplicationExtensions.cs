using Comuki.Modules.Artifacts.Application.Packaging;
using Comuki.Shared.Contracts.Artifacts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Modules.Artifacts.Application;

/// <summary>
/// Composition of the Artifacts application layer: the packager service,
/// its in-module driver helper and the bookkeeping ports it uses. The
/// default <see cref="IRunArtifactBundleStore"/> is the no-op stub
/// (always reports <em>not bundled</em>) — the host replaces it with the
/// EF-backed implementation over the <c>artifacts</c> schema via
/// <see cref="TryAddSingleton{TService,TImplementation}"/>.
///
/// The actual <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>
/// driver lives in the host (see
/// <c>Comuki.Host.Artifacts.RunArtifactPackagerHostService</c>) so the
/// journal event emission stays outside the artifacts module — the
/// module has no project reference on the engine.
/// </summary>
public static class ArtifactsApplicationExtensions
{
    /// <summary>
    /// Registers the packager port, the in-module polling helper and a
    /// no-op bundle store. The bundle store and the artifact store are
    /// replaced by the host before the background service starts (no-op
    /// stubs satisfy the contract for tests that compose the module in
    /// isolation).
    /// </summary>
    /// <param name="services"></param>
    public static IServiceCollection AddArtifactsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRunArtifactBundleStore, NullRunArtifactBundleStore>();
        services.TryAddSingleton<IRunArtifactStore, NullRunArtifactStore>();
        services.TryAddSingleton<IRunArtifactJournalSource, NullRunArtifactJournalSource>();
        services.TryAddSingleton<IRunArtifactRunSource, NullRunArtifactRunSource>();
        services.AddSingleton<RunArtifactPackager>();
        services.AddSingleton<RunArtifactPackagerService>();
        return services;
    }
}
