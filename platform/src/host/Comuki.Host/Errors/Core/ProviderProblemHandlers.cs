using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Host.Errors.Core;

// Built-in registry rows for the kernel provider family, byte-identical to
// the mapping table they replace: the fixed-detail arms (timeout, upstream
// base) keep their fixed sentences; the message arms read the instance.

/// <summary>Kernel default: upstream timed out → 504 with the fixed timeout sentence.</summary>
internal sealed class ProviderTimeoutProblemHandler()
    : ProblemHandler<ProviderTimeoutException>(StatusCodes.Status504GatewayTimeout, "Upstream timeout")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(ProviderTimeoutException exception)
    {
        return Row("the upstream service timed out", exception.Code);
    }
}

/// <summary>Kernel default: upstream resource absent → 404 carrying the exception's message.</summary>
internal sealed class ProviderNotFoundProblemHandler()
    : ProblemHandler<ProviderNotFoundException>(StatusCodes.Status404NotFound, "Resource not found")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(ProviderNotFoundException exception)
    {
        return Row(exception.Message, exception.Code);
    }
}

/// <summary>Kernel default: upstream refused the request → 403 carrying the exception's message.</summary>
internal sealed class ProviderForbiddenProblemHandler()
    : ProblemHandler<ProviderForbiddenException>(StatusCodes.Status403Forbidden, "Forbidden")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(ProviderForbiddenException exception)
    {
        return Row(exception.Message, exception.Code);
    }
}

/// <summary>Kernel default: upstream unavailable → 502 with the fixed unavailability sentence.</summary>
internal sealed class ProviderExceptionProblemHandler()
    : ProblemHandler<ProviderException>(StatusCodes.Status502BadGateway, "Upstream unavailable")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(ProviderException exception)
    {
        return Row("the upstream service is unavailable", exception.Code);
    }
}

/// <summary>Kernel default: spend crossed a USD cap → 402 carrying the exception's message.</summary>
internal sealed class BudgetExceededProblemHandler()
    : ProblemHandler<BudgetExceededException>(StatusCodes.Status402PaymentRequired, "Budget exceeded")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(BudgetExceededException exception)
    {
        return Row(exception.Message, exception.Code);
    }
}
