using Comuki.Modules.Intake.Application.Sources;
using Comuki.Modules.Intake.Application.Tickets;
using Comuki.Shared.Kernel.Secrets;

namespace Comuki.Host.Errors.Handlers.Intake;

// Wire rows for the Intake module's domain errors, taken verbatim from the
// retired per-module runner (Intake) arms they replace: status, title, detail sentences
// (the ticket-conflict row keeps its fixed "already has an active run"
// sentence — the exception's own message names the status and is log-only)
// and the code spellings. Only the `type` URN changed spelling — it is now
// derived from the code (design D4) instead of being absent from the row.

/// <summary>Intake ticket row absent → 404.</summary>
internal sealed class IntakeTicketNotFoundProblemHandler()
    : ProblemHandler<IntakeTicketNotFoundException>(StatusCodes.Status404NotFound, "Intake ticket not found")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(IntakeTicketNotFoundException exception)
    {
        return Row(exception.Message, exception.Code);
    }
}

/// <summary>Source connection row absent → 404.</summary>
internal sealed class SourceConnectionNotFoundProblemHandler()
    : ProblemHandler<SourceConnectionNotFoundException>(StatusCodes.Status404NotFound, "Source connection not found")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(SourceConnectionNotFoundException exception)
    {
        return Row(exception.Message, exception.Code);
    }
}

/// <summary>Admission rule row absent → 404.</summary>
internal sealed class AdmissionRuleNotFoundProblemHandler()
    : ProblemHandler<AdmissionRuleNotFoundException>(StatusCodes.Status404NotFound, "Admission rule not found")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(AdmissionRuleNotFoundException exception)
    {
        return Row(exception.Message, exception.Code);
    }
}

/// <summary>Secret reference resolved to nothing → 400 (operator misconfiguration, not a server fault).</summary>
internal sealed class SecretRefUnsetProblemHandler()
    : ProblemHandler<SecretRefUnsetException>(StatusCodes.Status400BadRequest, "Secret env var is unset")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(SecretRefUnsetException exception)
    {
        return Row(exception.Message, "intake.secret_env_ref_unset");
    }
}

/// <summary>Ticket not in the claimable Pending state → 409 with the fixed claim-contention sentence.</summary>
internal sealed class IntakeTicketConflictProblemHandler()
    : ProblemHandler<IntakeTicketConflictException>(StatusCodes.Status409Conflict, "Ticket not claimable")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(IntakeTicketConflictException exception)
    {
        return Row("the ticket already has an active run", exception.Code);
    }
}
