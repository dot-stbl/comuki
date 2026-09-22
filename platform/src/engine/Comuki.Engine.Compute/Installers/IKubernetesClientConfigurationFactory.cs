using k8s;

namespace Comuki.Engine.Compute.Installers;

/// <summary>
/// Builds the <see cref="KubernetesClientConfiguration"/> for the in-process
/// <c>IKubernetes</c> client. Lives behind an interface so unit tests can
/// substitute a controlled build function (the real
/// <see cref="KubernetesClientConfiguration.InClusterConfig"/> reads the pod's
/// SA token, which is environment-dependent).
/// </summary>
public interface IKubernetesClientConfigurationFactory
{
    /// <summary>
    ///     Returns the configuration for <c>IKubernetes</c>, or null when
    ///     <paramref name="skipInCluster"/> is true and <paramref name="kubeconfigPath"/>
    ///     is empty/whitespace — the installer wires a no-op client in that
    ///     case. Throws <see cref="Exceptions.KubernetesConfigUnavailableException"/>
    ///     when an in-cluster config is required but unavailable
    ///     (missing <c>KUBERNETES_SERVICE_HOST</c> / token); a non-empty
    ///     <paramref name="kubeconfigPath"/> still resolves through the file
    ///     path delegate regardless of <paramref name="skipInCluster"/>.
    /// </summary>
    public KubernetesClientConfiguration? Build(string? kubeconfigPath, bool skipInCluster);
}
