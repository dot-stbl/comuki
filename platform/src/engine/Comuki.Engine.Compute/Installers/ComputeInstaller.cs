using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Pool;
using Comuki.Engine.Compute.Ports;
using Comuki.Engine.Compute.Providers;
using Comuki.Engine.Compute.Providers.Kubernetes;
using Comuki.Engine.Compute.Security.Stores;
using Comuki.Engine.Compute.Settings;
using Comuki.Engine.Compute.Supervisor;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Contracts.Compute;
using Docker.DotNet;
using k8s;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
/// <see cref="Security.WorkerTokenIssuer"/> is registered BOTH here and by
/// <c>WorkerRuntimeExtensions.AddWorkerRuntime</c> on the host side —
/// each as <c>TryAddSingleton</c>, so whichever runs first wins and the
/// duplicate is a no-op rather than a throw. The dual site is deliberate:
/// hosts that compose through <c>HostComposer</c> without the worker
/// runtime (integration fixtures) still resolve the supervisor's
/// dependency graph.
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

        // Build identity of the running host: the source the supervisor's
        // worker-image pinning (WorkerImagePinning) derives the tag from.
        services.AddSingleton(ComukiBuildInfo.Read());

        services.AddSingleton<IWorkerTokenStore, InMemoryWorkerTokenStore>();
        services.TryAddSingleton<Security.WorkerTokenIssuer>();
        // Docker client (Enhanced 4.x): one Docker.DotNet assembly in the
        // process — Testcontainers references the same Enhanced line
        // transitively, and the classic 3.x package alongside it produced
        // a TypeLoadException at runtime. The provider consumes only
        // IContainerOperations, so that is the registered surface.
        services.AddSingleton(static _ => new DockerClientBuilder().Build());
        services.AddSingleton(static serviceProvider =>
            serviceProvider.GetRequiredService<DockerClient>().Containers);

        // Kubernetes client: reads Compute:Kubernetes:KubeconfigPath when set
        // (external cluster, e.g. vega), otherwise falls back to the default
        // config chain (in-cluster SA when running inside a cluster, or
        // ~/.kube/config locally). Failures are logged loudly — a silent DI
        // crash leaves the scale supervisor dead with no trace.
        services.AddSingleton<IKubernetes>(static serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Comuki.Compute.KubernetesClient");
            var kubeconfigPath = serviceProvider
                .GetRequiredService<IOptions<KubernetesComputeOptions>>()
                .Value.KubeconfigPath;
            try
            {
                var config = string.IsNullOrWhiteSpace(kubeconfigPath)
                    ? KubernetesClientConfiguration.BuildDefaultConfig()
                    : KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeconfigPath);
                logger.LogInformation(
                    "Kubernetes client ready ({Mode}), host: {Host}",
                    string.IsNullOrWhiteSpace(kubeconfigPath) ? "in-cluster" : kubeconfigPath,
                    config.Host);
                return new Kubernetes(config);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Kubernetes client failed to initialise (kubeconfig: {KubeconfigPath})",
                    kubeconfigPath ?? "(default)");
                throw;
            }
        });
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
        services.AddSingleton<IComukiWorker, ScaleSupervisorComukiWorker>();

        return services;
    }
}
