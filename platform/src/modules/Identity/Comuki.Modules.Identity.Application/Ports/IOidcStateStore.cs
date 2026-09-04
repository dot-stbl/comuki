using Comuki.Modules.Identity.Domain.Oidc;

namespace Comuki.Modules.Identity.Application.Ports;

/// <summary>
/// Persistence port for OIDC state rows: a small single-use store that
/// binds a <c>state</c> token (URL-safe UUIDv7) to the PKCE verifier
/// and the in-app <c>returnTo</c> a flow was started for.
/// </summary>
public interface IOidcStateStore
{
    /// <summary>Persists a new state row and returns the issued token.</summary>
    /// <param name="state"></param>
    /// <param name="cancellationToken"></param>
    public Task SaveAsync(OidcState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the row by <paramref name="id"/>, deletes it atomically, and
    /// returns the snapshot if it is still alive. A stale row (past
    /// <see cref="OidcState.ExpiresAt"/>) reads as <c>null</c> even if
    /// the row exists — the same single-use guarantee either way.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    public Task<OidcState?> ConsumeAsync(OidcStateId id, CancellationToken cancellationToken = default);

    /// <summary>Best-effort sweep of expired rows.</summary>
    /// <param name="now"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The number of rows deleted.</returns>
    public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
