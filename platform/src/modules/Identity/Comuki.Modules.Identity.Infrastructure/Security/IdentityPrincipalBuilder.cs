using System.Globalization;
using System.Security.Claims;
using Comuki.Modules.Identity.Domain.Users;

namespace Comuki.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Builds the <see cref="ClaimsPrincipal"/> shapes the two schemes
/// produce, so a handler downstream sees one principal grammar no matter
/// how the caller authenticated.
/// </summary>
public static class IdentityPrincipalBuilder
{
    /// <summary>The principal a successful login cookie carries.</summary>
    /// <param name="user"></param>
    /// <returns></returns>
    public static ClaimsPrincipal BuildForCookie(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.Value.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            new(IdentityClaimNames.TokensVersion, user.TokensVersion.ToString(CultureInfo.InvariantCulture)),
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.Cookie));
    }
}
