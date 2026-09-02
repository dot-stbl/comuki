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
    [Fact]
    public void ResolveDockerProviderByDefault()
    {
        using var provider = BuildProvider([]);

        provider.GetRequiredService<IComputeProvider>().ShouldBeOfType<DockerComputeProvider>();
    }

    [Fact]
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
        _ = services.AddComukiCompute(configuration);

        // Substitute both SDK clients AFTER the installer so neither the
        // docker socket nor a kubeconfig is touched when the concretes
        // resolve — the selection factory itself is what is under test.
        _ = services.AddSingleton(Substitute.For<IDockerClient>());
        _ = services.AddSingleton(Substitute.For<IKubernetes>());

        return services.BuildServiceProvider();
    }
}
