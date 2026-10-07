using Comuki.Host.Errors;
using Comuki.Host.Errors.Handlers.Integration;
using Comuki.Modules.Integrations.Application.Sources;
using Comuki.Modules.Integrations.Application.Tickets;
using Comuki.Modules.Integrations.Domain.Ids;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Errors;

/// <summary>
/// Wire rows for the Integrations module's domain errors (domain-error-contract
/// task 3.1). Every literal below — status, type URN, title, detail sentence
/// and code — is pinned from the <c>IntegrationEndpointRunner</c> arms these
/// handlers replace, so the runner deletion cannot drift from the shape the
/// FE already consumes. The one deliberate delta: the <c>type</c> URN is new
/// (the runner set none) and derived from the code (design D4).
/// </summary>
public sealed class IntegrationProblemHandlersShould
{
    [Fact(DisplayName = "Given an unknown ticket id, when the InboundItemNotFound handler answers, then the row pins the runner's 404 shape")]
    public void InboundItemNotFoundPinsRunnerShape()
    {
        var ticketId = new InboundItemId(Guid.Parse("0d1c3a57-8e2b-4f18-9d4a-6b7e5c2a1f00"));
        var handler = new InboundItemNotFoundProblemHandler();

        var answer = handler.Answer(new InboundItemNotFoundException(ticketId));

        answer.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        answer.Type.ShouldBe("urn:comuki:error:integration.inbound_item_not_found");
        answer.Title.ShouldBe("Integration inbound item not found");
        answer.Detail.ShouldBe($"inbound item '{ticketId}' not found");
        answer.Code.ShouldBe("integration.inbound_item_not_found");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("integration.inbound_item_not_found");
        answer.ValidationErrors.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an unknown connection id, when the ConnectionNotFound handler answers, then the row pins the runner's 404 shape")]
    public void SourceConnectionNotFoundPinsRunnerShape()
    {
        var connectionId = new SourceConnectionId(Guid.Parse("1e2d4b68-9f3c-4029-8e5b-7c8f6d3b2a11"));
        var handler = new SourceConnectionNotFoundProblemHandler();

        var answer = handler.Answer(new SourceConnectionNotFoundException(connectionId));

        answer.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        answer.Type.ShouldBe("urn:comuki:error:integration.connection_not_found");
        answer.Title.ShouldBe("Source connection not found");
        answer.Detail.ShouldBe($"source connection '{connectionId}' not found");
        answer.Code.ShouldBe("integration.connection_not_found");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("integration.connection_not_found");
    }

    [Fact(DisplayName = "Given an unknown admission rule id, when the RuleNotFound handler answers, then the row pins the runner's 404 shape")]
    public void AdmissionRuleNotFoundPinsRunnerShape()
    {
        var ruleId = new AdmissionRuleId(Guid.Parse("2f3e5c79-8a4d-413a-9f6c-8d9a7e4c3b22"));
        var handler = new AdmissionRuleNotFoundProblemHandler();

        var answer = handler.Answer(new AdmissionRuleNotFoundException(ruleId));

        answer.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        answer.Type.ShouldBe("urn:comuki:error:integration.rule_not_found");
        answer.Title.ShouldBe("Admission rule not found");
        answer.Detail.ShouldBe($"admission rule '{ruleId}' not found");
        answer.Code.ShouldBe("integration.rule_not_found");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("integration.rule_not_found");
    }

    [Fact(DisplayName = "Given an unset secret reference, when the SecretRefUnset handler answers, then the row pins the runner's 400 shape")]
    public void SecretRefUnsetPinsRunnerShape()
    {
        var handler = new SecretRefUnsetProblemHandler();

        var answer = handler.Answer(new SecretRefUnsetException("env:GH_TOKEN"));

        answer.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        answer.Type.ShouldBe("urn:comuki:error:integration.secret_env_ref_unset");
        answer.Title.ShouldBe("Secret env var is unset");
        answer.Detail.ShouldBe("secret reference 'env:GH_TOKEN' is unset on the host");
        answer.Code.ShouldBe("integration.secret_env_ref_unset");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("integration.secret_env_ref_unset");
    }

    [Fact(DisplayName = "Given a ticket not in Pending, when the InboundItemConflict handler answers, then the row keeps the fixed contention sentence")]
    public void InboundItemConflictPinsFixedSentence()
    {
        var ticketId = new InboundItemId(Guid.Parse("3a4f6d80-9b5e-424b-8a7d-9e0b8f5d4c33"));
        var handler = new InboundItemConflictProblemHandler();

        var answer = handler.Answer(new InboundItemConflictException(ticketId, "Running"));

        answer.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        answer.Type.ShouldBe("urn:comuki:error:integration.inbound_item_conflict");
        answer.Title.ShouldBe("Inbound item not claimable");
        answer.Detail.ShouldBe("the inbound item already has an active run");
        answer.Code.ShouldBe("integration.inbound_item_conflict");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("integration.inbound_item_conflict");
    }

    [Fact(DisplayName = "Given the Integration rows registered, when an InboundItemConflictException resolves, then the registry serves the module row")]
    public void IntegrationRowsResolveThroughRegistry()
    {
        var registry = new ProblemHandlerRegistry(
        [
            new InboundItemNotFoundProblemHandler(),
            new SourceConnectionNotFoundProblemHandler(),
            new AdmissionRuleNotFoundProblemHandler(),
            new SecretRefUnsetProblemHandler(),
            new InboundItemConflictProblemHandler(),
        ],
        NullLogger<ProblemHandlerRegistry>.Instance);

        var answer = registry.Resolve(new SecretRefUnsetException("env:TRACKER_TOKEN"));

        answer.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        answer.Title.ShouldBe("Secret env var is unset");
        answer.Code.ShouldBe("integration.secret_env_ref_unset");
    }
}
