namespace Comuki.Shared.Kernel.Exceptions;

/// <summary>
/// The project's spend crossed a configured USD cap. Maps to HTTP
/// <c>402 Payment Required</c> via the central <c>ProviderExceptionHandler</c>
/// — semantically the right status for a "budget exhausted" outcome
/// (RFC 9110 §15.5.4) rather than 502 (upstream unavailable) or
/// 429 (rate limit).
/// </summary>
/// <param name="code">
/// Stable dot.case identifier (<c>budget.hard_exceeded</c> or
/// <c>budget.soft_exceeded</c>). The hard variant maps to 402; the
/// soft variant still surfaces to ProblemDetails for parity even when
/// the caller chose to proceed.
/// </param>
/// <param name="message">
/// Safe human message — no PII, no raw USD figures in the title (the
/// spent / cap pair rides in <c>extensions</c> when the caller wants it).
/// </param>
/// <param name="inner">Optional upstream cause for log-only use.</param>
public sealed class BudgetExceededException(
    string code,
    string message,
    Exception? inner = null)
    : ProviderException(code, message, inner);
