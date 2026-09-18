using k8s;

namespace Comuki.Engine.Compute.Installers;

internal static class KubernetesClientConfigurationFactory
{
    public static KubernetesClientConfiguration Build(string? kubeconfigPath)
    {
        return Build(
            kubeconfigPath,
            KubernetesClientConfiguration.InClusterConfig,
            static path => KubernetesClientConfiguration.BuildConfigFromConfigFile(path));
    }

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
