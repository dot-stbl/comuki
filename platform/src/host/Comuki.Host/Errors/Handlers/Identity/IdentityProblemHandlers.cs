using Comuki.Modules.Identity.Application.Users;

namespace Comuki.Host.Errors.Handlers.Identity;

// Wire row for the identity-admin surface's domain error, taken verbatim
// from the deleted UsersController catch arm it replaces: status, title, the
// exception's own detail sentence and the offending-email extension the FE
// invite form surfaces. The `type` URN spelling is byte-identical — the
// catch already hand-wrote `urn:comuki:error:user.oidc_link_exists` and the
// code-derived URN (design D4) produces the same string.

/// <summary>Invite email already maps to an OIDC-linked user (Q43) → 409 with the offending <c>email</c> riding along.</summary>
internal sealed class OidcLinkConflictProblemHandler()
    : ProblemHandler<OidcLinkConflictException>(StatusCodes.Status409Conflict, "Email already linked to an OIDC identity")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(OidcLinkConflictException exception)
    {
        return Row(
            exception.Message,
            exception.Code,
            new Dictionary<string, object?> { ["email"] = exception.Email });
    }
}
