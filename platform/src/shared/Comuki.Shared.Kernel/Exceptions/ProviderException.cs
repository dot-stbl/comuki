namespace Comuki.Shared.Kernel.Exceptions;

/// <summary>
/// Boundary failure for an upstream / provider / Refit client: network,
/// mapping or 5xx from the upstream service. Carries a stable
/// machine-readable <see cref="Code"/> that the central
/// <c>ProviderExceptionHandler</c> maps to <c>application/problem+json</c>
/// (HTTP 502 by default; subclasses raise 504 / 404). The base is open
/// because it documents a stable extension point (subclasses exist);
/// subclasses are sealed.
/// </summary>
/// <param name="code">
/// Stable dot.case identifier (<c>provider.network_error</c>,
/// <c>provider.timeout</c>, <c>order.not_found</c>). Clients branch on
/// <c>Code</c>, never on <c>Message</c>.
/// </param>
/// <param name="message">
/// Safe human message — no stack, no secret, no PII (mirrors the
/// ProblemDetails <c>detail</c> contract from
/// <c>problem-details.md</c> §2).
/// </param>
/// <param name="inner">Optional upstream cause for log-only use.</param>
public class ProviderException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>Stable dot.case code surfaced as the ProblemDetails <c>code</c> extension.</summary>
    public string Code { get; } = code;
}
