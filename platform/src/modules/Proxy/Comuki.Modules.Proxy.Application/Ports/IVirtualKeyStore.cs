using Comuki.Modules.Proxy.Application.Models;

namespace Comuki.Modules.Proxy.Application.Ports;

/// <summary>
/// Looks up virtual keys by their bearer token. The default implementation
/// (<see cref="Resolving.ConfigurationVirtualKeyStore"/>) reads from
/// <c>Proxy:VirtualKeys</c> at startup; a Postgres- or file-backed
/// implementation can replace it without touching the auth handler.
/// </summary>
public interface IVirtualKeyStore
{
    /// <summary>Returns the matching key or <c>null</c> when no row matches.</summary>
    /// <param name="token">Raw token from <c>Authorization: Bearer &lt;token&gt;</c>.</param>
    /// <param name="cancellationToken"></param>
    public Task<VirtualKey?> FindAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Returns every key — used by the model catalogue and health checks.</summary>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<VirtualKey>> ListAsync(CancellationToken cancellationToken = default);
}
