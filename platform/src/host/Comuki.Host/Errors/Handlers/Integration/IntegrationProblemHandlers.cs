using Comuki.Modules.Integrations.Application.Sources;
using Comuki.Modules.Integrations.Application.Tickets;
using Comuki.Shared.Kernel.Secrets;

namespace Comuki.Host.Errors.Handlers.Integration;

// Wire rows for the Integrations module's domain errors, taken verbatim from
// the retired per-module runner (Integrations) arms they replace: status, title,
// detail sentences (the inbound-item-conflict row keeps its fixed "already
// has an active run" sentence — the exception's own message names the status
// and is log-only) and the code spellings. Only the `type` URN changed
// spelling — it is now derived from the code (design D4) instead of being
// absent from the row.

/// <summary>Integration inbound item row absent → 404.</summary>
internal sealed class InboundItemNotFoundProblemHandler()
    : ProblemHandler<InboundItemNotFoundException>(StatusCodes.Status404NotFound, "Integration inbound item not found")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(InboundItemNotFoundException exception)
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
        return Row(exception.Message, "integration.secret_env_ref_unset");
    }
}

/// <summary>Inbound item not in the claimable Pending state → 409 with the fixed claim-contention sentence.</summary>
internal sealed class InboundItemConflictProblemHandler()
    : ProblemHandler<InboundItemConflictException>(StatusCodes.Status409Conflict, "Inbound item not claimable")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(InboundItemConflictException exception)
    {
        return Row("the inbound item already has an active run", exception.Code);
    }
}
