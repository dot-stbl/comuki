namespace Comuki.Shared.Kernel.Exceptions;

/// <summary>
/// The requested resource is absent upstream (404 from the provider, or
/// an explicit "not found" branch in the provider's mapping). Maps to
/// HTTP 404 via the central <c>ProviderExceptionHandler</c>; carries the
/// upstream's <c>code</c> through unchanged.
/// </summary>
/// <param name="code">Stable dot.case identifier (defaults to <c>provider.not_found</c>).</param>
/// <param name="message">Safe human message — no PII.</param>
/// <param name="inner">Optional upstream cause for log-only use.</param>
public sealed class ProviderNotFoundException(
    string code = "provider.not_found",
    string message = "the upstream resource was not found",
    Exception? inner = null)
    : ProviderException(code, message, inner);
