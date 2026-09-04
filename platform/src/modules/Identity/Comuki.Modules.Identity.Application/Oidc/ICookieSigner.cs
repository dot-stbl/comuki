using Comuki.Modules.Identity.Application.Views;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Host-side cookie sign-in contract for the OIDC callback handler.
/// The host implements this — it owns the cookie scheme and the
/// identity principal builder; the application-layer callback
/// handler stays free of ASP.NET plumbing.
/// </summary>
public interface ICookieSigner
{
    /// <summary>Signs the user in via the cookie scheme so subsequent requests carry the session.</summary>
    /// <param name="user">Local account view from the linker.</param>
    /// <param name="cancellationToken"></param>
    public Task SignInAsync(UserAccountView user, CancellationToken cancellationToken = default);
}
