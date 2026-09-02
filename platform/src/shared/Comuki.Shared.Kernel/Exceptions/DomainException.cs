namespace Comuki.Shared.Kernel.Exceptions;

/// <summary>
/// A semantic domain invariant was violated (uniqueness, state
/// transition, precondition). The central <c>ProviderExceptionHandler</c>
/// maps this base (and every subclass — the type tree is walked once) to
/// HTTP 422, distinguishing semantic failure from upstream failure
/// (502/504). Throw from aggregate / factory code, not from controllers
/// — controllers stay clean of error plumbing (per
/// <c>problem-details.md</c> §1).
/// </summary>
/// <param name="code">
/// Stable dot.case identifier (<c>domain.conflict</c>,
/// <c>identity.user_not_found</c>). Clients branch on <c>Code</c>.
/// </param>
/// <param name="message">Safe human message — no PII, no secrets.</param>
/// <param name="inner">Optional root cause for log-only use.</param>
public class DomainException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>Stable dot.case code surfaced as the ProblemDetails <c>code</c> extension.</summary>
    public string Code { get; } = code;
}
