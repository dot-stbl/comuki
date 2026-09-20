using k8s;

namespace Comuki.Engine.Compute.Installers;

/// <summary>
/// Default <see cref="IKubernetesClientConfigurationFactory"/>: strict in-cluster
/// when <c>KubeconfigPath</c> is empty/whitespace, file-based otherwise.
/// BuildDefaultConfig is intentionally not used — its final fallback targets
/// <c>http://localhost:8080</c>, which would silently mask a missing
/// ServiceAccount mount.
/// </summary>
internal sealed class KubernetesClientConfigurationFactory : IKubernetesClientConfigurationFactory
{
    public KubernetesClientConfiguration Build(string? kubeconfigPath)
    {
        return Build(
            kubeconfigPath,
            KubernetesClientConfiguration.InClusterConfig,
            static path => KubernetesClientConfiguration.BuildConfigFromConfigFile(path));
    }

    /// <summary>
    /// Test seam: lets unit tests supply their own in-cluster / file-build
    /// delegates without touching the real <c>k8s</c> SDK calls.
    /// </summary>
    public static KubernetesClientConfiguration Build(
        string? kubeconfigPath,
        Func<KubernetesClientConfiguration> buildInCluster,
        Func<string, KubernetesClientConfiguration> buildFromConfigFile)
    {
        return string.IsNullOrWhiteSpace(kubeconfigPath)
            ? buildInCluster()
            : buildFromConfigFile(kubeconfigPath);
    }
}
