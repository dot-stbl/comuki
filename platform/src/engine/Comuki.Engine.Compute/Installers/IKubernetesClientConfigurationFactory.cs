using k8s;

namespace Comuki.Engine.Compute.Installers;

/// <summary>
/// Builds the <see cref="KubernetesClientConfiguration"/> for the in-process
/// <c>IKubernetes</c> client. Lives behind an interface so unit tests can
/// substitute a controlled build function (the real
/// <see cref="KubernetesClientConfiguration.InClusterConfig"/> reads the pod's
/// SA token, which is environment-dependent).
/// </summary>
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
    ///     Returns the configuration for <c>IKubernetes</c>:
    ///     <list type="bullet">
    ///         <item>empty/whitespace <paramref name="kubeconfigPath"/> → in-cluster (strict, throws when SA token is missing);</item>
    ///         <item>non-empty path → reads that kubeconfig file.</item>
    ///     </list>
    /// </summary>
    public KubernetesClientConfiguration Build(string? kubeconfigPath);
}
