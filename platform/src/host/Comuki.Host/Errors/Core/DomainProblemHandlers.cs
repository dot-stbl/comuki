using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Host.Errors.Core;

// The two generic rows every other exception lands on: a DomainException
// (or unmapped subclass) answers 422 with its own code; anything unrecognized
// stays opaque — 500, about:blank, no stack or secret in the body.

/// <summary>Kernel default: semantic domain rule violated → 422 with the instance's own code and URN.</summary>
internal sealed class DomainExceptionProblemHandler()
    : ProblemHandler<DomainException>(StatusCodes.Status422UnprocessableEntity, "Domain rule violated")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(DomainException exception)
    {
        return Row(exception.Message, exception.Code);
    }
}

/// <summary>Kernel default: unhandled exception → opaque 500, <c>about:blank</c>, generic detail.</summary>
internal sealed class FallbackProblemHandler : IProblemHandler
{
    /// <inheritdoc />
    public Type ExceptionType => typeof(Exception);

    /// <inheritdoc />
    public ProblemAnswer Answer(Exception exception)
    {
        return ProblemAnswer.For(
            StatusCodes.Status500InternalServerError,
            "Internal server error",
            "an unexpected error occurred",
            "internal.error") with
        { Type = "about:blank" };
    }
}
