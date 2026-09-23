namespace Comuki.Engine.Compute.Exceptions;

/// <summary>
/// Raised by a compute provider when the worker egress fence cannot be
/// applied (Docker: the fenced network is missing or not internal;
/// Kubernetes: the per-worker NetworkPolicy cannot be created) and
/// <c>Compute:AllowUnfencedEgress</c> is false. Fail-closed: no container
/// or Job is created — the caller (scale supervisor pass) records this
/// typed error and retries on the next tick.
/// </summary>
/// <param name="message">Safe human message naming the fence gap and the two remediations (fix the fence, or set the dev override).</param>
/// <param name="inner">Optional transport/API root cause for log-only use.</param>
public sealed class ComputeFenceException(string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>
    /// Stable machine code of the fence refusal. Callers and journal
    /// entries branch on it, never on <see cref="Exception.Message"/>.
    /// </summary>
    public const string ErrorCode = "compute.fence.unavailable";
}
