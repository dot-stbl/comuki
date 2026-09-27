using Comuki.Engine.Orchestration.Domain;
using Comuki.Host.Errors;
using Comuki.Host.Errors.Handlers.Runs;
using Comuki.Host.Runs;
using Comuki.Shared.Filtering.Parser;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Errors;

/// <summary>
/// Wire rows for the Runs surface's domain errors (domain-error-contract
/// task 6.1). Every literal below — status, type URN, title, detail
/// sentence, code and the decision-conflict extensions — is pinned from the
/// <c>RunsEndpointRunner</c> arms these handlers replace, so the runner
/// deletion cannot drift from the shape the FE already consumes. The one
/// deliberate delta: the <c>type</c> URN is new (the runner set none) and
/// derived from the code (design D4).
/// </summary>
public sealed class RunsProblemHandlersShould
{
    [Fact(DisplayName = "Given a decision on a terminal run, when the StateConflict handler answers, then the row keeps the currentStatus and decision extensions")]
    public void RunDecisionConflictPinsRunnerShape()
    {
        var handler = new RunDecisionConflictProblemHandler();

        var answer = handler.Answer(new RunDecisionConflictException(RunStatus.Succeeded, RunStatus.Escalated, "approve"));

        answer.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        answer.Type.ShouldBe("urn:comuki:error:run.terminal_state");
        answer.Title.ShouldBe("Run state conflict");
        answer.Detail.ShouldBe("run in Succeeded cannot be approved (would land on Escalated)");
        answer.Code.ShouldBe("run.terminal_state");
        answer.Extensions.Count.ShouldBe(3);
        answer.Extensions["code"].ShouldBe("run.terminal_state");
        answer.Extensions["currentStatus"].ShouldBe("Succeeded");
        answer.Extensions["decision"].ShouldBe("approve");
        answer.ValidationErrors.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an unparseable filter expression, when the FilterParse handler answers, then the row pins the runner's 400 shape")]
    public void FilterParsePinsRunnerShape()
    {
        var handler = new FilterParseProblemHandler();

        var answer = handler.Answer(new FilterParseException("Unexpected token 'status=='", 8));

        answer.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        answer.Type.ShouldBe("urn:comuki:error:filter.invalid");
        answer.Title.ShouldBe("Invalid filter expression");
        answer.Detail.ShouldBe("Unexpected token 'status==' (at position 8)");
        answer.Code.ShouldBe("filter.invalid");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("filter.invalid");
    }

    [Fact(DisplayName = "Given the Runs rows registered, when a FilterParseException resolves, then the registry serves the module row over the 500 fallback")]
    public void RunsRowsResolveThroughRegistry()
    {
        var registry = new ProblemHandlerRegistry(
        [
            new RunDecisionConflictProblemHandler(),
            new FilterParseProblemHandler(),
        ],
        NullLogger<ProblemHandlerRegistry>.Instance);

        var answer = registry.Resolve(new FilterParseException("Expected ')'"));

        answer.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        answer.Title.ShouldBe("Invalid filter expression");
    }
}
