using Comuki.Modules.Identity.Application.Oidc;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;

namespace Comuki.Host.Auth.Security;

/// <summary>
/// Host-side cookie signer: <see cref="IUserAccountStore"/> resolves
/// the linked local account by id; the host signs the cookie via the
/// ambient HttpContext using the module's
/// <see cref="IdentityPrincipalBuilder.BuildForCookie"/>. The OIDC
/// callback handler stays free of ASP.NET plumbing — this adapter is
/// the seam.
/// </summary>
/// <param name="userStore">Resolves the linked local account by id.</param>
/// <param name="httpContextAccessor">Ambient HTTP context for the cookie write.</param>
public sealed class CookieSignerAdapter(
    IUserAccountStore userStore,
    IHttpContextAccessor httpContextAccessor) : ICookieSigner
{
    /// <inheritdoc />
    public async Task SignInAsync(UserAccountView user, CancellationToken cancellationToken = default)
    {
        var account = await userStore.FindByIdAsync(user.Id, cancellationToken)
            ?? throw new InvalidOperationException($"oidc link resolved user {user.Id} that is now missing");

        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("an HTTP request context is required for cookie session operations");

        await httpContext.SignInAsync(AuthSchemes.Cookie, IdentityPrincipalBuilder.BuildForCookie(account));
    }
}
