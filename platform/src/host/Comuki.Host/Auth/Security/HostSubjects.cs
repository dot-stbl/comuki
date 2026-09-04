using System.Security.Claims;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Identity.Infrastructure.Security;

namespace Comuki.Host.Auth.Security;

/// <summary>
/// Principal → <see cref="RoleSubject"/> for the host surface: the
/// api-key claim resolves to the key subject, otherwise the
/// nameidentifier claim resolves to the user subject. Mirrors the
/// resolver the Identity enforcement filter uses.
/// </summary>
public static class HostSubjects
{
    /// <summary>Resolves the caller's subject from the claims principal.</summary>
    /// <param name="principal"></param>
    /// <returns></returns>
    public static RoleSubject? Resolve(ClaimsPrincipal principal)
    {
        return OfClaim(IdentityClaimNames.ApiKeyId, SubjectType.ApiKey, principal)
            ?? OfClaim(ClaimTypes.NameIdentifier, SubjectType.User, principal);
    }

    /// <summary>The owning user id of the caller — null when the caller is an api key without an owner stamp (rare).</summary>
    /// <param name="principal"></param>
    /// <returns></returns>
    public static Guid? OwnerUserIdOf(ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value is { Length: > 0 } value
            && Guid.TryParse(value, out var userId)
            ? userId
            : null;
    }

    private static RoleSubject? OfClaim(string claimName, SubjectType type, ClaimsPrincipal principal)
    {
        return principal.FindFirst(claimName)?.Value is { Length: > 0 } value
            && Guid.TryParse(value, out var id)
            ? new RoleSubject(type, id)
            : null;
    }
}
