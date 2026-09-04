using Comuki.Modules.Proxy.Application.Models;

namespace Comuki.Modules.Proxy.Application.Ports;

/// <summary>
/// Reports whether a virtual key is still inside its monthly USD budget.
/// The caller (<see cref>comuki.host.proxy</see> middleware) consults the
/// result before forwarding a request; on hard exceedance the call is
/// rejected with 429 and a Retry-After header.
/// </summary>
public interface IProxyBudgetEnforcer
{
    /// <summary>
    /// Returns the verdict plus the cap and the spend that produced it
    /// (USD micros). The cap is <c>null</c> when the key carries no
    /// monthly limit.
    /// </summary>
    /// <param name="key">Key the caller presented.</param>
    /// <param name="cancellationToken"></param>
    public Task<ProxyBudgetVerdict> EvaluateAsync(VirtualKey key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Snapshot the proxy's pre-flight check returns: <see cref="Allowed"/>
/// decides whether to forward; <see cref="CapUsdMicros"/> / <see cref="SpentUsdMicros"/>
/// are diagnostic figures surfaced on 429 responses.
/// </summary>
/// <param name="Allowed">When <c>false</c> the proxy rejects the call with 429.</param>
/// <param name="CapUsdMicros">Configured monthly cap; <c>null</c> means no cap.</param>
/// <param name="SpentUsdMicros">Sum of recorded <c>proxy</c>-source usage this calendar month.</param>
/// <param name="RetryAfterSeconds">Seconds until the start of next month (UTC); 0 when unlimited.</param>
public sealed record ProxyBudgetVerdict(
    bool Allowed,
    long? CapUsdMicros,
    long SpentUsdMicros,
    int RetryAfterSeconds);
