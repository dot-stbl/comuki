using Comuki.Engine.Compute.Exceptions;
using k8s;
using k8s.Exceptions;

namespace Comuki.Engine.Compute.Installers;

/// <summary>
/// Default <see cref="IKubernetesClientConfigurationFactory"/>: explicit
/// kubeconfig path wins; otherwise honours <c>SkipInCluster</c> (returns
/// null) or builds in-cluster and re-throws <c>KubernetesClientException</c>
/// as a typed <see cref="KubernetesConfigUnavailableException"/> with an
/// actionable hint. BuildDefaultConfig is intentionally not used — its
/// final fallback targets <c>http://localhost:8080</c>, which would silently
/// mask a missing ServiceAccount mount.
/// </summary>
internal sealed class KubernetesClientConfigurationFactory : IKubernetesClientConfigurationFactory
{
    public KubernetesClientConfiguration? Build(string? kubeconfigPath, bool skipInCluster)
    {
        return Build(
            kubeconfigPath,
            skipInCluster,
            KubernetesClientConfiguration.InClusterConfig,
            static path => KubernetesClientConfiguration.BuildConfigFromConfigFile(path));
    }

    /// <summary>
    /// Test seam: lets unit tests supply their own in-cluster / file-build
    /// delegates without touching the real <c>k8s</c> SDK calls. The catch
    /// wires the SDK's <c>KubernetesClientException</c> to the typed
    /// <see cref="KubernetesConfigUnavailableException"/> so the host's
    /// caller-visible message is the actionable one.
    /// </summary>
    public static KubernetesClientConfiguration? Build(
        string? kubeconfigPath,
        bool skipInCluster,
        Func<KubernetesClientConfiguration> buildInCluster,
        Func<string, KubernetesClientConfiguration> buildFromConfigFile)
    {
        if (!string.IsNullOrWhiteSpace(kubeconfigPath))
        {
            return buildFromConfigFile(kubeconfigPath);
        }

        if (skipInCluster)
        {
            return null;
        }

        try
        {
            return buildInCluster();
        }
        catch (KubernetesClientException exception)
        {
            throw new KubernetesConfigUnavailableException(BuildHintMessage(), exception);
        }
    }

    private static string BuildHintMessage()
    {
        return "Kubernetes in-cluster configuration is unavailable "
            + "(KUBERNETES_SERVICE_HOST / KUBERNETES_SERVICE_PORT / service account token "
            + "are missing on this pod). Either set Compute:Provider=docker if you are not "
            + "deploying workers from this host, set automountServiceAccountToken: true on the "
            + "Deployment so the ServiceAccount token is mounted, or supply an explicit "
            + "Compute:Kubernetes:KubeconfigPath. As a last resort, set "
            + "Compute:Kubernetes:SkipKubernetesConfig=true to register a no-op client.";
    }
}
