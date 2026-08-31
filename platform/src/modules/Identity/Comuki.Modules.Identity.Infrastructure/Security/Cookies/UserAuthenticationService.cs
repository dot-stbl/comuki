using System.Globalization;
using System.Security.Claims;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Sessions;
using Comuki.Modules.Identity.Domain.Ids;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Comuki.Modules.Identity.Infrastructure.Security.Cookies;

/// <summary>
/// Default <see cref="IUserAuthenticationService"/>: local login through
/// the pure <see cref="LoginHandler"/>, then a signed-in cookie carrying
/// the account id and security stamp. The stamp (tokens_version) is what
/// makes a password change or disable kill every outstanding session.
/// </summary>
/// <param name="loginHandler"></param>
/// <param name="userStore"></param>
/// <param name="httpContextAccessor"></param>
public sealed class UserAuthenticationService(
    LoginHandler loginHandler,
    IUserAccountStore userStore,
    IHttpContextAccessor httpContextAccessor) : IUserAuthenticationService
{
    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(LoginCommand command, CancellationToken cancellationToken = default)
    {
        var result = await loginHandler.HandleAsync(command, cancellationToken);

        if (result is { Success: true, UserId: { } userId })
        {
            var user = await userStore.FindByIdAsync(userId, cancellationToken)
                ?? throw new InvalidOperationException($"login succeeded for missing user {userId}");

            var httpContext = CookieHttpContext.Require(httpContextAccessor);

            await httpContext.SignInAsync(AuthSchemes.Cookie, IdentityPrincipalBuilder.BuildForCookie(user));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = CookieHttpContext.Require(httpContextAccessor);

        await httpContext.SignOutAsync(AuthSchemes.Cookie);
    }

    /// <inheritdoc />
    public async Task<bool> ValidateCookieAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var versionClaim = principal.FindFirst(IdentityClaimNames.TokensVersion)?.Value;

        if (userIdClaim is null || versionClaim is null)
        {
            return false;
        }

        if (!Guid.TryParse(userIdClaim, out var rawUserId)
            || !int.TryParse(versionClaim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokensVersion))
        {
            return false;
        }

        var user = await userStore.FindByIdAsync(new UserId(rawUserId), cancellationToken);

        return user is { Disabled: false } && user.TokensVersion == tokensVersion;
    }
}

/// <summary>Fetches the ambient HTTP context or explains why there is none.</summary>
file static class CookieHttpContext
{
    public static HttpContext Require(IHttpContextAccessor accessor)
    {
        return accessor.HttpContext
            ?? throw new InvalidOperationException("an HTTP request context is required for cookie session operations");
    }
}
