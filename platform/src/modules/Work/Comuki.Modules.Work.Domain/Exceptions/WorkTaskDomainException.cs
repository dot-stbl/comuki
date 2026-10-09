using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Work.Domain.Exceptions;

/// <summary>
/// Work-domain invariant violation: a factory rejected its inputs or
/// an aggregate guard rejected a state transition. Sealed because
/// every site is known (the audit-listing — see
/// <see cref="WorkTaskErrorCodes"/>); new code paths get a new sealed
/// subclass if they need a different shape, not a generic new throw.
/// Extends the kernel <see cref="DomainException"/> so the central
/// exception handler walks the type tree once and maps every
/// Work-domain violation to HTTP 422 with the stable <c>Code</c> as
/// the ProblemDetails <c>code</c> extension (per
/// <c>error-mapping.md</c> §4 — semantic / unprocessable).
/// </summary>
/// <param name="code">Stable dot.case identifier from <see cref="WorkTaskErrorCodes"/>; clients branch on <c>Code</c>, never on <c>Message</c>.</param>
/// <param name="message">Human-readable description of the invariant violation; safe to surface in ProblemDetails <c>detail</c> (no PII, no secrets — see <c>exceptions.md</c> §4).</param>
/// <param name="inner">Optional underlying cause; carried by <see cref="Exception.InnerException"/> for the central handler's structured log, not surfaced in <c>detail</c>.</param>
public sealed class WorkTaskDomainException(string code, string message, Exception? inner = null)
    : DomainException(code, message, inner)
{
}
