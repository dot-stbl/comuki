using System.Security.Claims;
using Comuki.Modules.Identity.Application.Sessions;

namespace Comuki.Modules.Identity.Infrastructure.Security.Cookies;

/// <summary>
/// Cookie session plumbing exposed as a service — the Host wires
/// controllers and the cookie events onto this, keeping the module free
/// of any HTTP surface. Login/Logout operate on the ambient
/// <see cref="Microsoft.AspNetCore.Http.HttpContext"/>; ValidateCookie
/// is the security-stamp recheck the cookie event calls per request.
/// </summary>
public interface IUserAuthenticationService
{
    /// <summary>Verifies credentials and signs in the cookie on success.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<LoginResult> LoginAsync(LoginCommand command, CancellationToken cancellationToken = default);

    /// <summary>Signs out the cookie.</summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rechecks a cookie principal against the account's current
    /// <c>tokens_version</c> and disabled flag; false rejects the cookie.
    /// </summary>
    /// <param name="principal"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<bool> ValidateCookieAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
