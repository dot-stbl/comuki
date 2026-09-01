using System.Security.Claims;
using Comuki.Modules.Identity.Infrastructure.Security;

namespace Comuki.Host.Chat;

/// <summary>
/// Principal → owning subject id for the chat surface: the api-key claim
/// resolves to the key subject, otherwise the nameidentifier claim resolves
/// to the user subject. Mirrors the resolver the auth controller keeps
/// file-private by design; returns a plain <see cref="Guid"/> because chat
/// sessions only need the owning identity, not its type.
/// </summary>
internal static class ChatSubjects
{
    /// <summary>Resolves the acting subject id; throws when unauthenticated (the permission filter runs first).</summary>
    /// <param name="principal">Authenticated principal.</param>
    public static Guid ResolveSubjectId(ClaimsPrincipal principal)
    {
        return OfClaim(principal, IdentityClaimNames.ApiKeyId)
            ?? OfClaim(principal, ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("chat endpoints require an authenticated subject (RequiresPermission enforces it)");
    }

    /// <summary>Reads one claim as a guid; null when absent or unparsable.</summary>
    /// <param name="principal"></param>
    /// <param name="claimName"></param>
    public static Guid? OfClaim(ClaimsPrincipal principal, string claimName)
    {
        return principal.FindFirst(claimName)?.Value is { Length: > 0 } value
            && Guid.TryParse(value, out var id)
            ? id
            : null;
    }
}
