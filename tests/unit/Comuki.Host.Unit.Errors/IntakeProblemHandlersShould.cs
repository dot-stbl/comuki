using Comuki.Host.Errors;
using Comuki.Host.Errors.Handlers.Intake;
using Comuki.Modules.Intake.Application.Sources;
using Comuki.Modules.Intake.Application.Tickets;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Errors;

/// <summary>
/// Wire rows for the Intake module's domain errors (domain-error-contract
/// task 3.1). Every literal below — status, type URN, title, detail sentence
/// and code — is pinned from the <c>IntakeEndpointRunner</c> arms these
/// handlers replace, so the runner deletion cannot drift from the shape the
/// FE already consumes. The one deliberate delta: the <c>type</c> URN is new
/// (the runner set none) and derived from the code (design D4).
/// </summary>
public sealed class IntakeProblemHandlersShould
{
    [Fact(DisplayName = "Given an unknown ticket id, when the TicketNotFound handler answers, then the row pins the runner's 404 shape")]
    public void IntakeTicketNotFoundPinsRunnerShape()
    {
        var ticketId = new IncomingTicketId(Guid.Parse("0d1c3a57-8e2b-4f18-9d4a-6b7e5c2a1f00"));
        var handler = new IntakeTicketNotFoundProblemHandler();

        var answer = handler.Answer(new IntakeTicketNotFoundException(ticketId));

        answer.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        answer.Type.ShouldBe("urn:comuki:error:intake.ticket_not_found");
        answer.Title.ShouldBe("Intake ticket not found");
        answer.Detail.ShouldBe($"intake ticket '{ticketId}' not found");
        answer.Code.ShouldBe("intake.ticket_not_found");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("intake.ticket_not_found");
        answer.ValidationErrors.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an unknown connection id, when the ConnectionNotFound handler answers, then the row pins the runner's 404 shape")]
    public void SourceConnectionNotFoundPinsRunnerShape()
    {
        var connectionId = new SourceConnectionId(Guid.Parse("1e2d4b68-9f3c-4029-8e5b-7c8f6d3b2a11"));
        var handler = new SourceConnectionNotFoundProblemHandler();

        var answer = handler.Answer(new SourceConnectionNotFoundException(connectionId));

        answer.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        answer.Type.ShouldBe("urn:comuki:error:intake.connection_not_found");
        answer.Title.ShouldBe("Source connection not found");
        answer.Detail.ShouldBe($"source connection '{connectionId}' not found");
        answer.Code.ShouldBe("intake.connection_not_found");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("intake.connection_not_found");
    }

    [Fact(DisplayName = "Given an unknown admission rule id, when the RuleNotFound handler answers, then the row pins the runner's 404 shape")]
    public void AdmissionRuleNotFoundPinsRunnerShape()
    {
        var ruleId = new AdmissionRuleId(Guid.Parse("2f3e5c79-8a4d-413a-9f6c-8d9a7e4c3b22"));
        var handler = new AdmissionRuleNotFoundProblemHandler();

        var answer = handler.Answer(new AdmissionRuleNotFoundException(ruleId));

        answer.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        answer.Type.ShouldBe("urn:comuki:error:intake.rule_not_found");
        answer.Title.ShouldBe("Admission rule not found");
        answer.Detail.ShouldBe($"admission rule '{ruleId}' not found");
        answer.Code.ShouldBe("intake.rule_not_found");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("intake.rule_not_found");
    }

    [Fact(DisplayName = "Given an unset secret reference, when the SecretRefUnset handler answers, then the row pins the runner's 400 shape")]
    public void SecretRefUnsetPinsRunnerShape()
    {
        var handler = new SecretRefUnsetProblemHandler();

        var answer = handler.Answer(new SecretRefUnsetException("env:GH_TOKEN"));

        answer.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        answer.Type.ShouldBe("urn:comuki:error:intake.secret_env_ref_unset");
        answer.Title.ShouldBe("Secret env var is unset");
        answer.Detail.ShouldBe("secret reference 'env:GH_TOKEN' is unset on the host");
        answer.Code.ShouldBe("intake.secret_env_ref_unset");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("intake.secret_env_ref_unset");
    }

    [Fact(DisplayName = "Given a ticket not in Pending, when the TicketConflict handler answers, then the row keeps the fixed contention sentence")]
    public void IntakeTicketConflictPinsFixedSentence()
    {
        var ticketId = new IncomingTicketId(Guid.Parse("3a4f6d80-9b5e-424b-8a7d-9e0b8f5d4c33"));
        var handler = new IntakeTicketConflictProblemHandler();

        var answer = handler.Answer(new IntakeTicketConflictException(ticketId, "Running"));

        answer.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        answer.Type.ShouldBe("urn:comuki:error:intake.ticket_conflict");
        answer.Title.ShouldBe("Ticket not claimable");
        answer.Detail.ShouldBe("the ticket already has an active run");
        answer.Code.ShouldBe("intake.ticket_conflict");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("intake.ticket_conflict");
    }

    [Fact(DisplayName = "Given the Intake rows registered, when an IntakeTicketConflictException resolves, then the registry serves the module row")]
    public void IntakeRowsResolveThroughRegistry()
    {
        var registry = new ProblemHandlerRegistry(
        [
            new IntakeTicketNotFoundProblemHandler(),
            new SourceConnectionNotFoundProblemHandler(),
            new AdmissionRuleNotFoundProblemHandler(),
            new SecretRefUnsetProblemHandler(),
            new IntakeTicketConflictProblemHandler(),
        ],
        NullLogger<ProblemHandlerRegistry>.Instance);

        var answer = registry.Resolve(new SecretRefUnsetException("env:TRACKER_TOKEN"));

        answer.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        answer.Title.ShouldBe("Secret env var is unset");
        answer.Code.ShouldBe("intake.secret_env_ref_unset");
    }
}
