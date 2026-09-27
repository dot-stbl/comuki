using Comuki.Host.Chat.Brain;
using Comuki.Host.Errors;
using Comuki.Host.Errors.Handlers.Chat;
using Comuki.Modules.Chat.Application.Sessions;
using Comuki.Modules.Chat.Domain.Ids;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Errors;

/// <summary>
/// Wire rows for the Chat surface's domain errors (domain-error-contract
/// task 5.1). Every literal below — status, type URN, title, detail sentence
/// and code — is pinned from the <c>ChatEndpointRunner</c> arms these
/// handlers replace, so the runner deletion cannot drift from the shape the
/// FE already consumes. The one deliberate delta: the <c>type</c> URN is new
/// (the runner set none) and derived from the code (design D4).
/// </summary>
public sealed class ChatProblemHandlersShould
{
    [Fact(DisplayName = "Given a session waiting on approve, when the ApprovePending handler answers, then the row pins the runner's 409 shape")]
    public void ChatApprovePendingPinsRunnerShape()
    {
        var sessionId = new ChatSessionId(Guid.Parse("0d1c3a57-8e2b-4f18-9d4a-6b7e5c2a1f00"));
        var handler = new ChatApprovePendingProblemHandler();

        var answer = handler.Answer(new ChatApprovePendingException(sessionId));

        answer.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        answer.Type.ShouldBe("urn:comuki:error:chat.approve_pending");
        answer.Title.ShouldBe("Plan approval pending");
        answer.Detail.ShouldBe($"chat session '{sessionId}' is waiting for a plan approve/reject decision");
        answer.Code.ShouldBe("chat.approve_pending");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("chat.approve_pending");
        answer.ValidationErrors.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a brain call that did not complete, when the BrainUnavailable handler answers, then the row carries the problem code, not the gRPC status name")]
    public void BrainUnavailablePinsProblemCode()
    {
        var handler = new BrainUnavailableProblemHandler();

        var answer = handler.Answer(new BrainUnavailableException("UNAVAILABLE", "the brain process did not answer in time"));

        answer.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        answer.Type.ShouldBe("urn:comuki:error:chat.brain_unavailable");
        answer.Title.ShouldBe("Brain unavailable");
        answer.Detail.ShouldBe("the brain process did not answer in time");
        answer.Code.ShouldBe("chat.brain_unavailable");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("chat.brain_unavailable");
    }

    [Fact(DisplayName = "Given the Chat rows registered, when a BrainUnavailableException resolves, then the registry serves the module row over the 500 fallback")]
    public void ChatRowsResolveThroughRegistry()
    {
        var registry = new ProblemHandlerRegistry(
        [
            new ChatApprovePendingProblemHandler(),
            new BrainUnavailableProblemHandler(),
        ],
        NullLogger<ProblemHandlerRegistry>.Instance);

        var answer = registry.Resolve(new BrainUnavailableException("DEADLINE_EXCEEDED", "deadline expired"));

        answer.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        answer.Title.ShouldBe("Brain unavailable");
    }
}
