using Comuki.Host.Errors;
using Comuki.Host.Errors.Handlers.Identity;
using Comuki.Modules.Identity.Application.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Errors;

/// <summary>
/// Wire row for the identity-admin surface's domain error (domain-error-contract,
/// UsersController arm). Every literal below — status, type URN, title, detail
/// sentence and extensions — is pinned from the catch block this handler
/// replaces (<c>UsersController.InviteUserAsync</c>, Q43), so the catch
/// deletion cannot drift from the shape the FE invite form already consumes.
/// The URN is byte-identical: the catch hand-wrote the same
/// <c>urn:comuki:error:</c> spelling the registry now derives from the code
/// (design D4).
/// </summary>
public sealed class IdentityProblemHandlersShould
{
    [Fact(DisplayName = "Given an OIDC-linked email on invite, when the OidcLinkConflict handler answers, then the row pins the catch's 409 shape")]
    public void OidcLinkConflictPinsCatchShape()
    {
        var handler = new OidcLinkConflictProblemHandler();

        var answer = handler.Answer(new OidcLinkConflictException("ada@example.com"));

        answer.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        answer.Type.ShouldBe("urn:comuki:error:user.oidc_link_exists");
        answer.Title.ShouldBe("Email already linked to an OIDC identity");
        answer.Detail.ShouldBe("user with email 'ada@example.com' is already linked to an OIDC identity; invite refused");
        answer.Code.ShouldBe("user.oidc_link_exists");
        answer.Extensions.Count.ShouldBe(2);
        answer.Extensions["code"].ShouldBe("user.oidc_link_exists");
        answer.Extensions["email"].ShouldBe("ada@example.com");
        answer.ValidationErrors.ShouldBeNull();
    }

    [Fact(DisplayName = "Given the Identity row registered, when an OidcLinkConflictException resolves, then the registry serves the module row, not the 422 default")]
    public void IdentityRowResolvesThroughRegistry()
    {
        var registry = new ProblemHandlerRegistry(
            [new OidcLinkConflictProblemHandler()],
            NullLogger<ProblemHandlerRegistry>.Instance);

        var answer = registry.Resolve(new OidcLinkConflictException("ada@example.com"));

        answer.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        answer.Title.ShouldBe("Email already linked to an OIDC identity");
        answer.Extensions["email"].ShouldBe("ada@example.com");
    }
}
