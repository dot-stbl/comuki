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

    /// <summary>Persists a new link.</summary>
    /// <param name="link"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task SaveAsync(OidcLink link, CancellationToken cancellationToken = default);
}
