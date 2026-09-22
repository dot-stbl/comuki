using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Engine.Orchestration.Domain.Exceptions;

/// <summary>
/// Engine-domain invariant violation: a factory rejected its inputs or
/// an aggregate guard rejected a state transition. Sealed because every
/// site is known (the audit-listing — see <see cref="OrchestrationErrorCodes"/>);
/// new code paths get a new sealed subclass if they need a different
/// shape, not a generic new throw. Extends the kernel
/// <see cref="DomainException"/> so the central exception handler walks
/// the type tree once and maps every orchestrator-domain violation to
/// HTTP 422 with the stable <c>Code</c> as the ProblemDetails
/// <c>code</c> extension (per <c>error-mapping.md</c> §4 — semantic /
/// unprocessable).
/// </summary>
/// <param name="code">
/// Stable dot.case identifier from <see cref="OrchestrationErrorCodes"/>.
/// Clients branch on <c>Code</c>, never on <c>Message</c>.
/// </param>
/// <param name="message">Safe human message — no PII, no secrets.</param>
/// <param name="inner">Optional root cause for log-only use.</param>
public sealed class OrchestrationDomainException(string code, string message, Exception? inner = null)
    : DomainException(code, message, inner)
{
}
