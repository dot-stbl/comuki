namespace Comuki.Shared.Kernel.Exceptions;

/// <summary>
/// The subject is known but not allowed to proceed — disabled, revoked
/// or otherwise ineligible. Maps to HTTP 403 via the central
/// <c>ProviderExceptionHandler</c>; carries the upstream's <c>code</c>
/// through unchanged.
/// </summary>
/// <param name="code">Stable dot.case identifier (defaults to <c>user.disabled</c>).</param>
/// <param name="message">Safe human message — no PII.</param>
/// <param name="inner">Optional upstream cause for log-only use.</param>
public sealed class ProviderForbiddenException(
    string code = "user.disabled",
    string message = "the subject is not allowed to proceed",
    Exception? inner = null)
    : ProviderException(code, message, inner);
