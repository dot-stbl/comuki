using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Pool;
using Comuki.Engine.Compute.Ports;
using Comuki.Engine.Compute.Providers;
using Comuki.Engine.Compute.Providers.Kubernetes;
using Comuki.Engine.Compute.Security.Stores;
using Comuki.Engine.Compute.Settings;
using Comuki.Engine.Compute.Supervisor;
using Comuki.Shared.Contracts.Compute;
using Docker.DotNet;
using k8s;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Compute.Installers;

/// <summary>
/// Registers the compute engine (issue #3 T2.4/T2.5, S8 k8s provider): both
/// compute providers behind a config-selected <see cref="IComputeProvider"/>,
/// the in-memory worker-token store, and the scale supervisor. Wired only in
/// a host composition root — nothing else references these concretes. The
/// host must ALSO register an <see cref="IBacklogReader"/> (the Orchestration
/// queue adapter lands with the queue slice); without it the supervisor
/// resolution fails fast.
/// </summary>
/// <remarks>
/// <see cref="WorkerTokenIssuer"/> itself is registered by
/// <c>WorkerRuntimeExtensions.AddWorkerRuntime</c> on the host side
/// (<c>TryAddSingleton</c>) — historically this method registered it as
/// well, which made the order of <c>AddComukiCompute</c> vs
/// <c>AddWorkerRuntime</c> load-bearing: whichever ran second would throw on
/// the duplicate. The host does not call <c>AddComukiCompute</c> from its
/// composition root today, so this method's issuer line was dead code; the
/// race-prone duplicate is gone and the canonical site is the host.
/// </remarks>
public static class ComputeInstaller
{
    /// <summary>Adds the compute engine: options, both providers with Compute:Provider selection, the in-memory worker-token store, scale supervisor.</summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    public static IServiceCollection AddComukiCompute(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ComputeOptions>()
            .Bind(configuration.GetSection(ComputeOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(static options => options.Provider is ComputeOptions.DockerProvider or ComputeOptions.KubernetesProvider,
                $"Compute:Provider must be '{ComputeOptions.DockerProvider}' or '{ComputeOptions.KubernetesProvider}'")
            .ValidateOnStart();

        services.AddOptions<DockerComputeOptions>()
            .Bind(configuration.GetSection(DockerComputeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<KubernetesComputeOptions>()
            .Bind(configuration.GetSection(KubernetesComputeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<WorkerTokenOptions>()
            .Bind(configuration.GetSection(WorkerTokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ScaleSupervisorOptions>()
            .Bind(configuration.GetSection(ScaleSupervisorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IWorkerTokenStore, InMemoryWorkerTokenStore>();
        services.AddSingleton<IDockerClient>(static _ => new DockerClientConfiguration().CreateClient());
        services.AddSingleton<IKubernetes>(static _ => new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig()));
        services.AddSingleton<DockerComputeProvider>();
        services.AddSingleton<KubernetesComputeProvider>();

        // Selection by config (factory, house-idiomatic): both concretes are
        // registered; Compute:Provider picks the active one. The unselected
        // provider is never constructed, so a docker-only machine does not
        // need a kubeconfig and vice versa.
        services.AddSingleton<IComputeProvider>(static serviceProvider =>
        {
            var configured = serviceProvider.GetRequiredService<IOptions<ComputeOptions>>().Value.Provider;
            return configured switch
            {
                ComputeOptions.KubernetesProvider => serviceProvider.GetRequiredService<KubernetesComputeProvider>(),
                _ => serviceProvider.GetRequiredService<DockerComputeProvider>(),
            };
        });

        services.AddSingleton<IProjectScaleSettings, InMemoryProjectScaleSettings>();
        services.AddSingleton<WorkerPoolState>();
        services.AddSingleton<IWorkerPoolState>(static serviceProvider => serviceProvider.GetRequiredService<WorkerPoolState>());
        services.AddSingleton<ScaleSupervisorCycle>();
        services.AddHostedService<ScaleSupervisorWorker>();

        return services;
    }
}
