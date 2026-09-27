using Comuki.Host.Errors;
using Comuki.Host.Errors.Handlers.Learning;
using Comuki.Modules.Memory.Application.Learning;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Modules.Memory.Domain.Learning;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Errors;

/// <summary>
/// Wire rows for the Learning surface's domain errors (domain-error-contract
/// task 7.1). Every literal below — status, type URN, title, detail sentence,
/// code and the decision-conflict extensions — is pinned from the
/// <c>LearningProblems.StateConflict</c> arm the handler replaces, so the
/// runner deletion cannot drift from the shape the FE already consumes. The
/// one deliberate delta: the <c>type</c> URN is new (the runner set none)
/// and derived from the code (design D4).
/// </summary>
public sealed class LearningProblemHandlersShould
{
    [Fact(DisplayName = "Given a decision on an already-decided candidate, when the StateConflict handler answers, then the row keeps the candidateId and currentStatus extensions")]
    public void LearningDecisionConflictPinsRunnerShape()
    {
        var candidateId = new LearningCandidateId(Guid.Parse("0d1c3a57-8e2b-4f18-9d4a-6b7e5c2a1f00"));
        var handler = new LearningDecisionConflictProblemHandler();

        var answer = handler.Answer(new LearningDecisionConflictException(candidateId, LearningStatus.Rejected));

        answer.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        answer.Type.ShouldBe("urn:comuki:error:learning.already_decided");
        answer.Title.ShouldBe("Learning candidate already decided");
        answer.Detail.ShouldBe($"learning candidate {candidateId.Value} is already rejected");
        answer.Code.ShouldBe("learning.already_decided");
        answer.Extensions.Count.ShouldBe(3);
        answer.Extensions["code"].ShouldBe("learning.already_decided");
        answer.Extensions["candidateId"].ShouldBe(candidateId.Value.ToString());
        answer.Extensions["currentStatus"].ShouldBe("rejected");
        answer.ValidationErrors.ShouldBeNull();
    }

    [Fact(DisplayName = "Given the Learning row registered, when a LearningDecisionConflictException resolves, then the registry serves the module row over the 500 fallback")]
    public void LearningRowsResolveThroughRegistry()
    {
        var registry = new ProblemHandlerRegistry(
            [new LearningDecisionConflictProblemHandler()],
            NullLogger<ProblemHandlerRegistry>.Instance);

        var answer = registry.Resolve(new LearningDecisionConflictException(
            new LearningCandidateId(Guid.NewGuid()),
            LearningStatus.Approved));

        answer.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        answer.Title.ShouldBe("Learning candidate already decided");
    }
}
