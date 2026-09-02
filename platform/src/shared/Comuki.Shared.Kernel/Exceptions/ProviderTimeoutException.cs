namespace Comuki.Shared.Kernel.Exceptions;

/// <summary>
/// Upstream timed out — request budget exhausted or socket-level read
/// exceeded the configured timeout. Maps to HTTP 504 via the central
/// <c>ProviderExceptionHandler</c>.
/// </summary>
/// <param name="code">Stable dot.case identifier (defaults to <c>provider.timeout</c>).</param>
/// <param name="message">Safe human message — no PII, no internal timing.</param>
/// <param name="inner">Optional upstream cause for log-only use.</param>
public sealed class ProviderTimeoutException(
    string code = "provider.timeout",
    string message = "the upstream service timed out",
    Exception? inner = null)
    : ProviderException(code, message, inner);
