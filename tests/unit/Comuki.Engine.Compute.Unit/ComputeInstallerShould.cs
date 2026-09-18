using Comuki.Engine.Compute.Installers;
using Comuki.Engine.Compute.Providers;
using Comuki.Engine.Compute.Providers.Kubernetes;
using Comuki.Shared.Contracts.Compute;
using Docker.DotNet;
using k8s;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Provider-selection wiring of <see cref="ComputeInstaller.AddComukiCompute"/>:
/// Compute:Provider picks the concrete behind <see cref="IComputeProvider"/>
/// (factory, both registered; the unselected one never constructed).
/// </summary>
public sealed class ComputeInstallerShould
{
    [Theory(DisplayName = "Given no kubeconfig path, when building Kubernetes configuration, then uses in-cluster configuration")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void UseInClusterConfigurationWithoutLocalhostFallback(string? kubeconfigPath)
    {
        var inClusterConfiguration = new KubernetesClientConfiguration
        {
            Host = "https://10.96.0.1",
        };

        var configuration = KubernetesClientConfigurationFactory.Build(
            kubeconfigPath,
            () => inClusterConfiguration,
            _ => throw new InvalidOperationException("External kubeconfig must not be read"));

        configuration.ShouldBeSameAs(inClusterConfiguration);
        configuration.Host.ShouldNotBe("http://localhost:8080");
    }

    [Fact(DisplayName = "Given explicit kubeconfig path, when building Kubernetes configuration, then reads that file")]
    public void UseExternalKubeconfigWhenPathIsExplicit()
    {
        const string KubeconfigPath = "clusters/worker.kubeconfig";
        string? observedPath = null;
        var externalConfiguration = new KubernetesClientConfiguration
        {
            Host = "https://worker.example.test",
        };

        var configuration = KubernetesClientConfigurationFactory.Build(
            KubeconfigPath,
            () => throw new InvalidOperationException("In-cluster configuration must not be read"),
            path =>
            {
                observedPath = path;
                return externalConfiguration;
            });

        configuration.ShouldBeSameAs(externalConfiguration);
        observedPath.ShouldBe(KubeconfigPath);
    }

    [Fact(DisplayName = "When resolve Docker Provider By Default, then test passes")]
    public void ResolveDockerProviderByDefault()
    {
        using var provider = BuildProvider([]);

        provider.GetRequiredService<IComputeProvider>().ShouldBeOfType<DockerComputeProvider>();
    }

    [Fact(DisplayName = "When resolve Kubernetes Provider When Configured, then test passes")]
    public void ResolveKubernetesProviderWhenConfigured()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Compute:Provider"] = "kubernetes",
        });

        provider.GetRequiredService<IComputeProvider>().ShouldBeOfType<KubernetesComputeProvider>();
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddComukiCompute(configuration);

        // Substitute both SDK clients AFTER the installer so neither the
        // docker socket nor a kubeconfig is touched when the concretes
        // resolve — the selection factory itself is what is under test.
        // The docker client itself is replaced; the container-operations
        // facade resolves from it without touching the docker socket.
        services.AddSingleton(Substitute.For<IContainerOperations>());
        services.AddSingleton(Substitute.For<IKubernetes>());

        return services.BuildServiceProvider();
    }
}
