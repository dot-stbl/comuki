namespace Comuki.Engine.Compute.Exceptions;

/// <summary>
/// Raised by <see cref="Installers.IKubernetesClientConfigurationFactory"/> when
/// no usable in-cluster configuration is available (the pod's ServiceAccount
/// token is not mounted — <c>KUBERNETES_SERVICE_HOST</c> /
/// <c>KUBERNETES_SERVICE_PORT</c> missing) and no explicit
/// <c>Compute:Kubernetes:KubeconfigPath</c> was supplied. Distinct from
/// <c>KubernetesClientException</c> so the host can render an actionable
/// remediation hint rather than the SDK's terse "token is missing" message.
/// </summary>
/// <remarks>
/// The host decides whether to fail fast (default) or downgrade to a
/// no-op client via <c>Compute:Kubernetes:SkipKubernetesConfig = true</c>
/// — when Skip is on, this exception is never raised.
/// </remarks>
public sealed class KubernetesConfigUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner)
{
}
