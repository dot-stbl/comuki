using Comuki.Modules.Identity.Domain.Users;

namespace Comuki.Modules.Identity.Application.Ports;

/// <summary>Persistence port for OIDC identity links.</summary>
public interface IOidcLinkStore
{
    /// <summary>Finds a link by provider + subject claim.</summary>
    /// <param name="provider"></param>
    /// <param name="subject"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<OidcLink?> FindAsync(string provider, string subject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an OIDC link for a user with the given email. Returns the
    /// first match when the email resolves to a local user that has any
    /// OIDC link — used by the invite path to refuse creating a local
    /// account for an email already bound to an external IdP (Q43).
    /// </summary>
    /// <param name="email">Lower-cased email the invitee would receive.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The first OIDC link for the user behind <paramref name="email"/>, or <c>null</c>.</returns>
    public Task<OidcLink?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Persists a new link.</summary>
    /// <param name="link"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task SaveAsync(OidcLink link, CancellationToken cancellationToken = default);
}
