using Comuki.Host.Errors;
using Comuki.Shared.Kernel.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Errors;

/// <summary>
/// Typed-registry resolution (domain-error-contract design D5/D9): the
/// exact runtime type is consulted first, then the base chain, kernel
/// defaults answer everything else, and wiring bugs (a non-sealed exact
/// type, a duplicate row) fail loudly at construction — never at request
/// time. The kernel-row literals below are the ones
/// <c>ExceptionMapping.Map</c> shipped; the integration suite exercises
/// the same rows end-to-end.
/// </summary>
public sealed class ProblemHandlerRegistryShould
{
    [Fact(DisplayName = "Given a sealed DomainException subclass with its own row, when resolved, then the module row beats the 422 default")]
    public void ModuleRowBeatsDomainDefault()
    {
        var registry = NewRegistry([new SealedDomainRowHandler()]);

        var answer = registry.Resolve(new SealedDomainException("test.sealed", "boom"));

        answer.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        answer.Code.ShouldBe("test.sealed");
        answer.Type.ShouldBe("urn:comuki:error:test.sealed");
    }

    [Fact(DisplayName = "Given a BudgetExceededException, when resolved, then its exact 402 row wins over the ProviderException 502 base row")]
    public void ExactKernelRowBeatsBaseChainRow()
    {
        var registry = NewRegistry([]);

        var answer = registry.Resolve(new BudgetExceededException("budget.hard_exceeded", "cap crossed"));

        answer.StatusCode.ShouldBe(StatusCodes.Status402PaymentRequired);
        answer.Title.ShouldBe("Budget exceeded");
        answer.Detail.ShouldBe("cap crossed");
        answer.Code.ShouldBe("budget.hard_exceeded");
    }

    [Fact(DisplayName = "Given a leaf exception whose middle base has no row, when resolved, then the chain is walked to the nearest registered ancestor")]
    public void BaseChainWalkedToNearestRow()
    {
        var registry = NewRegistry([new SealedDomainRowHandler()]);

        var answer = registry.Resolve(new LeafOfOpenException("test.chain", "from a subclass"));

        answer.StatusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
        answer.Code.ShouldBe("test.chain");
        answer.Type.ShouldBe("urn:comuki:error:test.chain");
    }

    [Fact(DisplayName = "Given a ProviderTimeoutException, when resolved, then it answers 504 with the fixed timeout sentence")]
    public void ProviderTimeoutAnswers504WithFixedSentence()
    {
        var registry = NewRegistry([]);

        var answer = registry.Resolve(new ProviderTimeoutException());

        answer.StatusCode.ShouldBe(StatusCodes.Status504GatewayTimeout);
        answer.Title.ShouldBe("Upstream timeout");
        answer.Detail.ShouldBe("the upstream service timed out");
        answer.Code.ShouldBe("provider.timeout");
        answer.Type.ShouldBe("urn:comuki:error:provider.timeout");
    }

    [Fact(DisplayName = "Given a ProviderNotFoundException, when resolved, then it answers 404 carrying the exception's message")]
    public void ProviderNotFoundAnswers404WithMessage()
    {
        var registry = NewRegistry([]);

        var answer = registry.Resolve(new ProviderNotFoundException("provider.not_found", "upstream resource 'foo' is gone"));

        answer.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        answer.Title.ShouldBe("Resource not found");
        answer.Detail.ShouldBe("upstream resource 'foo' is gone");
        answer.Code.ShouldBe("provider.not_found");
    }

    [Fact(DisplayName = "Given a base ProviderException, when resolved, then it answers 502 with the fixed unavailability sentence")]
    public void ProviderBaseAnswers502WithFixedSentence()
    {
        var registry = NewRegistry([]);

        var answer = registry.Resolve(new ProviderException("provider.network_error", "the upstream service did not respond"));

        answer.StatusCode.ShouldBe(StatusCodes.Status502BadGateway);
        answer.Title.ShouldBe("Upstream unavailable");
        answer.Detail.ShouldBe("the upstream service is unavailable");
        answer.Code.ShouldBe("provider.network_error");
    }

    [Fact(DisplayName = "Given a ProviderForbiddenException, when resolved, then it answers 403 carrying the exception's message")]
    public void ProviderForbiddenAnswers403WithMessage()
    {
        var registry = NewRegistry([]);

        var answer = registry.Resolve(new ProviderForbiddenException("provider.forbidden", "the upstream refused the key"));

        answer.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        answer.Title.ShouldBe("Forbidden");
        answer.Detail.ShouldBe("the upstream refused the key");
        answer.Code.ShouldBe("provider.forbidden");
    }

    [Fact(DisplayName = "Given an unhandled exception, when resolved, then it stays opaque: 500, about:blank, no stack in the detail")]
    public void UnhandledExceptionStaysOpaque()
    {
        var registry = NewRegistry([]);

        var answer = registry.Resolve(new InvalidOperationException("kaboom with a secret"));

        answer.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        answer.Type.ShouldBe("about:blank");
        answer.Title.ShouldBe("Internal server error");
        answer.Detail.ShouldBe("an unexpected error occurred");
        answer.Code.ShouldBe("internal.error");
        answer.Detail.ShouldNotContain("kaboom");
    }

    [Fact(DisplayName = "Given a handler registering a non-sealed exact type, when the registry is built, then construction fails naming the type")]
    public void NonSealedExactTypeFailsAtConstruction()
    {
        Should.Throw<InvalidOperationException>(static () => NewRegistry([new OpenDomainRowHandler()]))
            .Message.ShouldContain("OpenDomainException");
    }

    [Fact(DisplayName = "Given two handlers for the same exact type, when the registry is built, then construction fails naming both")]
    public void DuplicateRegistrationFailsAtConstruction()
    {
        Should.Throw<InvalidOperationException>(static () => NewRegistry([new SealedDomainRowHandler(), new DuplicateSealedDomainRowHandler()]))
            .Message.ShouldContain("SealedDomainException");
    }

    private static ProblemHandlerRegistry NewRegistry(IReadOnlyList<IProblemHandler> handlers)
    {
        return new ProblemHandlerRegistry(handlers, NullLogger<ProblemHandlerRegistry>.Instance);
    }

    /// <summary>A registered module row: sealed exact type, non-kernel status.</summary>
    private sealed class SealedDomainRowHandler()
        : ProblemHandler<SealedDomainException>(StatusCodes.Status409Conflict, "Sealed domain row")
    {
        /// <inheritdoc />
        protected override ProblemAnswer Answer(SealedDomainException exception)
        {
            return Row(exception.Message, exception.Code);
        }
    }

    /// <summary>A second row for <see cref="SealedDomainException"/> — exists only to trip the duplicate assertion.</summary>
    private sealed class DuplicateSealedDomainRowHandler()
        : ProblemHandler<SealedDomainException>(StatusCodes.Status409Conflict, "Duplicate")
    {
        /// <inheritdoc />
        protected override ProblemAnswer Answer(SealedDomainException exception)
        {
            return Row(exception.Message, exception.Code);
        }
    }

    /// <summary>A row for a non-sealed exact type — exists only to trip the sealed assertion.</summary>
    private sealed class OpenDomainRowHandler()
        : ProblemHandler<OpenDomainException>(StatusCodes.Status409Conflict, "Open domain row")
    {
        /// <inheritdoc />
        protected override ProblemAnswer Answer(OpenDomainException exception)
        {
            return Row(exception.Message, exception.Code);
        }
    }

    private sealed class SealedDomainException(string code, string message) : DomainException(code, message);

    /// <summary>Deliberately non-sealed middle of a chain with no row of its own.</summary>
    private class OpenDomainException(string code, string message) : DomainException(code, message);

    private sealed class LeafOfOpenException(string code, string message) : OpenDomainException(code, message);
}
